using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.AspNetCore;
using SquirrelBox.InMemory;

namespace SquirrelBox.AspNetCore.Tests;

public sealed class SquirrelBoxHttpInboxInternalTests
{
    private static readonly Type HttpInboxType = typeof(SquirrelBoxAspNetCoreOptions)
        .Assembly
        .GetType("SquirrelBox.AspNetCore.SquirrelBoxHttpInbox", throwOnError: true);
    private static readonly Type EndpointFilterExecutorType = typeof(SquirrelBoxAspNetCoreOptions)
        .Assembly
        .GetType("SquirrelBox.AspNetCore.SquirrelBoxInboxEndpointFilterExecutor", throwOnError: true);

    [Fact]
    public void HasInboxMetadata_detects_endpoint_and_mvc_action_metadata()
    {
        var httpContext = new DefaultHttpContext();

        Assert.False(Invoke<bool>("HasInboxMetadata", httpContext));

        httpContext.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new SquirrelBoxInboxAttribute()),
            "direct"));
        Assert.True(Invoke<bool>("HasInboxMetadata", httpContext));

        var action = new ControllerActionDescriptor
        {
            EndpointMetadata = [new SquirrelBoxInboxAttribute()]
        };
        httpContext.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(action),
            "mvc"));

        Assert.True(Invoke<bool>("HasInboxMetadata", httpContext));
    }

    [Fact]
    public void CreateOpenRequest_resolves_headers_payload_and_metadata_options()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = "trace-fallback";
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.Path = "/orders";
        httpContext.Request.Headers["X-Idempotency-Key"] = "key-1";
        httpContext.Request.Headers["X-Correlation-Id"] = "corr-1";
        var options = new SquirrelBoxAspNetCoreOptions();
        var metadata = new SquirrelBoxInboxAttribute
        {
            PolicyName = "orders",
            TtlSeconds = 5,
            DeferExecution = true,
            CompletedLock = InboxCompletedLockMode.Forever,
            DeferredLane = "orders",
            RetryMaxAttempts = 3
        };
        var payload = new TestPayload("1");

        var request = Invoke<InboxOpenRequest>(
            "CreateOpenRequest",
            httpContext,
            options,
            payload,
            null,
            null,
            null,
            null,
            metadata);

        Assert.Equal("http", request.Source);
        Assert.Equal("POST /orders", request.Operation);
        Assert.Equal("key-1", request.IdempotencyKey);
        Assert.Equal("X-Idempotency-Key", request.IdempotencyKeyName);
        Assert.Equal("corr-1", request.CorrelationId);
        Assert.Equal("trace-fallback", request.TraceId);
        Assert.Equal("orders", request.PolicyName);
        Assert.Equal(TimeSpan.FromSeconds(5), request.EntryLifetime);
        Assert.Equal(InboxExecutionMode.Deferred, request.ExecutionMode);
        Assert.Equal(InboxCompletedLockMode.Forever, request.CompletedLock);
        Assert.Equal("orders", request.Deferred.Lane);
        Assert.Equal(3, request.Deferred.MaxAttempts);
    }

    [Theory]
    [InlineData(InboxPolicyAction.Skip)]
    [InlineData(InboxPolicyAction.Replay)]
    [InlineData(InboxPolicyAction.Retry)]
    [InlineData(InboxPolicyAction.Fail)]
    public async Task WriteRejectedDecisionAsync_returns_conflict_payload_for_non_continue_actions(InboxPolicyAction action)
    {
        var httpContext = CreateJsonContext();
        var entry = CreateEntry();
        entry.CurrentAttempt = new InboxAttempt
        {
            AttemptId = "attempt-current",
            TraceId = "trace-current"
        };
        var decision = new InboxDecision(InboxOpenState.DuplicateInProgress, action, entry);

        await Invoke<Task>("WriteRejectedDecisionAsync", httpContext, decision);

        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        var body = ReadBody(httpContext);
        Assert.Contains(action.ToString(), body);
        Assert.Contains("attempt-current", body);
        Assert.Contains("trace-current", body);
    }

    [Fact]
    public async Task WritePayloadVerificationConflictAsync_suppresses_completion_and_writes_identity()
    {
        var httpContext = CreateJsonContext();
        var entry = CreateEntry();
        entry.LastAttemptId = "attempt-last";
        entry.LastTraceId = "trace-last";
        var result = new InboxPayloadVerificationResult(InboxPayloadVerificationState.PayloadConflict, entry);

        await Invoke<Task>("WritePayloadVerificationConflictAsync", httpContext, result);

        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        var body = ReadBody(httpContext);
        Assert.Contains(InboxPayloadVerificationState.PayloadConflict.ToString(), body);
        Assert.Contains(entry.Id.ToString(), body);
        Assert.Contains("attempt-last", body);
        Assert.Contains("trace-last", body);
    }

    [Fact]
    public async Task TryReplayDecisionAsync_respects_options_completion_and_replay_headers()
    {
        var options = new SquirrelBoxAspNetCoreOptions();
        var entry = CreateEntry();
        var decision = new InboxDecision(InboxOpenState.DuplicateCompleted, InboxPolicyAction.Replay, entry);

        options.ReplayCompletedResponses = false;
        Assert.False(await Invoke<Task<bool>>("TryReplayDecisionAsync", CreateJsonContext(), options, decision));

        options.ReplayCompletedResponses = true;
        Assert.False(await Invoke<Task<bool>>("TryReplayDecisionAsync", CreateJsonContext(), options, decision));

        entry.Completion = new InboxCompletion
        {
            StatusCode = StatusCodes.Status202Accepted,
            ContentType = "application/json",
            ResultPayload = Encoding.UTF8.GetBytes("{\"ok\":true}"),
            Headers =
            {
                ["ETag"] = "\"accepted\"",
                ["Content-Type"] = "ignored"
            }
        };
        var replayContext = CreateJsonContext();

        Assert.True(await Invoke<Task<bool>>("TryReplayDecisionAsync", replayContext, options, decision));

        Assert.Equal(StatusCodes.Status202Accepted, replayContext.Response.StatusCode);
        Assert.Equal("application/json", replayContext.Response.ContentType);
        Assert.Equal("\"accepted\"", replayContext.Response.Headers.ETag);
        Assert.Equal("{\"ok\":true}", ReadBody(replayContext));
    }

    [Fact]
    public void CreateCompletion_marks_large_bodies_and_captures_configured_headers()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.StatusCode = StatusCodes.Status201Created;
        httpContext.Response.ContentType = "text/plain";
        httpContext.Response.Headers.ETag = "\"too-large\"";
        var options = new SquirrelBoxAspNetCoreOptions
        {
            MaxReplayBodyBytes = 2
        };
        using var body = new MemoryStream(Encoding.UTF8.GetBytes("large"));

        var completion = Invoke<InboxCompletion>("CreateCompletion", httpContext, options, body);

        Assert.Equal(StatusCodes.Status201Created, completion.StatusCode);
        Assert.Equal("text/plain", completion.ContentType);
        Assert.Null(completion.ResultPayload);
        Assert.Equal("\"too-large\"", completion.Headers["ETag"]);
        Assert.Equal("true", completion.Metadata["squirrelbox:http:body-too-large"]);
    }

    [Fact]
    public void AttachIdentityHeaders_uses_configured_fallback_names()
    {
        var httpContext = new DefaultHttpContext();
        var options = new SquirrelBoxAspNetCoreOptions
        {
            ResponseHeaderName = "X-Idempotency"
        };
        var identity = new SquirrelBoxIdentity
        {
            Operation = new SquirrelBoxOperationIdentity
            {
                IdempotencyKey = SquirrelBoxMetadataValue.Create(null, "key", SquirrelBoxMetadataValueSource.Generated),
                CorrelationId = SquirrelBoxMetadataValue.Create("X-Correlation", "corr", SquirrelBoxMetadataValueSource.Generated)
            },
            Attempt = new SquirrelBoxAttemptIdentity
            {
                AttemptId = SquirrelBoxMetadataValue.Create("X-Attempt", "attempt", SquirrelBoxMetadataValueSource.Generated),
                TraceId = SquirrelBoxMetadataValue.Create("X-Trace", "trace", SquirrelBoxMetadataValueSource.Generated)
            }
        };

        Invoke<object>("AttachIdentityHeaders", httpContext, options, identity);

        Assert.Equal("key", httpContext.Response.Headers["X-Idempotency"]);
        Assert.Equal("corr", httpContext.Response.Headers["X-Correlation"]);
        Assert.Equal("attempt", httpContext.Response.Headers["X-Attempt"]);
        Assert.Equal("trace", httpContext.Response.Headers["X-Trace"]);
    }

    [Fact]
    public void EnsureIdentityHeadersOnStarting_registers_only_once()
    {
        var services = new ServiceCollection();
        services.AddSquirrelBox().UseInMemory();
        using var provider = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext();

        Invoke<object>(
            "EnsureIdentityHeadersOnStarting",
            httpContext,
            provider.GetRequiredService<IInboxService>(),
            new SquirrelBoxAspNetCoreOptions());
        Invoke<object>(
            "EnsureIdentityHeadersOnStarting",
            httpContext,
            provider.GetRequiredService<IInboxService>(),
            new SquirrelBoxAspNetCoreOptions());

        Assert.Single(httpContext.Items);
    }

    [Fact]
    public void ResolveCompletableContext_returns_null_when_completion_is_suppressed()
    {
        var httpContext = new DefaultHttpContext();

        Invoke<object>("SuppressCompletion", httpContext);
        var context = Invoke<InboxContext>(
            "ResolveCompletableContext",
            httpContext,
            null,
            null);

        Assert.Null(context);
    }

    [Fact]
    public async Task EndpointFilterExecutor_accepts_null_payload_without_opening_inbox()
    {
        var accepted = await InvokeExecutor<ValueTask<bool>>(
            "TryAcceptAsync",
            new DefaultHttpContext(),
            null,
            new SquirrelBoxInboxAttribute(),
            new FakeInboxService(),
            new FakePolicyResolver(new InboxDecision(InboxOpenState.Opened, InboxPolicyAction.Continue, new InboxEntry())),
            new SquirrelBoxAspNetCoreOptions());

        Assert.True(accepted);
    }

    [Fact]
    public async Task EndpointFilterExecutor_writes_payload_conflict_when_verification_fails()
    {
        var httpContext = CreateJsonContext();
        var entry = CreateEntry();
        var inbox = new FakeInboxService
        {
            OpenResult = new InboxOpenResult(InboxOpenState.Opened, entry: entry),
            VerificationResult = new InboxPayloadVerificationResult(InboxPayloadVerificationState.PayloadConflict, entry)
        };

        var accepted = await InvokeExecutor<ValueTask<bool>>(
            "TryAcceptAsync",
            httpContext,
            new TestPayload("conflict"),
            new SquirrelBoxInboxAttribute(),
            inbox,
            new FakePolicyResolver(new InboxDecision(InboxOpenState.Opened, InboxPolicyAction.Continue, entry)),
            new SquirrelBoxAspNetCoreOptions());

        Assert.False(accepted);
        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        Assert.Contains(InboxPayloadVerificationState.PayloadConflict.ToString(), ReadBody(httpContext));
    }

    [Fact]
    public void SquirrelBoxInboxAttribute_aliases_policy_window_and_blank_argument_name()
    {
        var attribute = new SquirrelBoxInboxAttribute(" ")
        {
            Policy = "orders",
            WindowSeconds = 7
        };

        Assert.Equal("request", attribute.ArgumentName);
        Assert.Equal("orders", attribute.PolicyName);
        Assert.Equal("orders", attribute.Policy);
        Assert.Equal(7, attribute.TtlSeconds);
        Assert.Equal(TimeSpan.FromSeconds(7), attribute.EntryLifetime);
    }

    [Fact]
    public async Task SquirrelBoxInboxAttribute_continues_pipeline_when_payload_argument_is_missing()
    {
        var attribute = new SquirrelBoxInboxAttribute("request");
        var httpContext = new DefaultHttpContext();
        var actionContext = new ActionContext(
            httpContext,
            new RouteData(),
            new ActionDescriptor(),
            new ModelStateDictionary());
        var executingContext = new ActionExecutingContext(
            actionContext,
            [],
            new Dictionary<string, object>(),
            controller: new object());
        var nextCalled = false;

        await attribute.OnActionExecutionAsync(
            executingContext,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(new ActionExecutedContext(actionContext, [], controller: new object()));
            });

        Assert.True(nextCalled);
        Assert.Null(executingContext.Result);
    }

    private static T Invoke<T>(string name, params object[] parameters)
    {
        var method = HttpInboxType.GetMethod(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(HttpInboxType.FullName, name);
        return (T)method.Invoke(null, parameters);
    }

    private static T InvokeExecutor<T>(string name, params object[] parameters)
    {
        var method = EndpointFilterExecutorType.GetMethod(name, BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMethodException(EndpointFilterExecutorType.FullName, name);
        return (T)method.Invoke(null, parameters);
    }

    private static DefaultHttpContext CreateJsonContext()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();
        return httpContext;
    }

    private static string ReadBody(HttpContext httpContext)
    {
        httpContext.Response.Body.Position = 0;
        using var reader = new StreamReader(httpContext.Response.Body, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static InboxEntry CreateEntry()
        => new()
        {
            Id = Ulid.NewUlid(),
            Source = "http",
            Operation = "POST /orders",
            IdempotencyKey = "key",
            IdempotencyKeyName = "Idempotency-Key",
            IdempotencyKeySource = InboxIdempotencyKeySource.Explicit,
            CorrelationId = "corr",
            CorrelationIdName = "Correlation-Id",
            CorrelationIdSource = SquirrelBoxMetadataValueSource.Generated,
            Status = InboxStatus.Completed,
            ExecutionMode = InboxExecutionMode.Inline,
            PolicyName = "default",
            CreatedOnUtc = DateTimeOffset.UtcNow,
            UpdatedOnUtc = DateTimeOffset.UtcNow
        };

    private sealed record TestPayload(string Id);

    private sealed class FakePolicyResolver(InboxDecision decision) : IInboxPolicyResolver
    {
        public InboxDecision Resolve(InboxOpenResult result)
            => decision;
    }

    private sealed class FakeInboxService : IInboxService
    {
        public InboxContext Current { get; set; }

        public InboxContext LastContext { get; set; }

        public InboxOpenResult OpenResult { get; set; } =
            new(InboxOpenState.Opened, entry: new InboxEntry());

        public InboxPayloadVerificationResult VerificationResult { get; set; } =
            new(InboxPayloadVerificationState.Verified, new InboxEntry());

        public ValueTask<InboxOpenResult> OpenOrContinueAsync(
            InboxOpenRequest request,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(OpenResult);

        public ValueTask<InboxContext> ContinueAsync(
            Ulid entryId,
            string owner = null,
            bool ownsCompletion = true,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Current);

        public ValueTask<InboxPayloadVerificationResult> VerifyCurrentPayloadAsync(
            object payload,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(VerificationResult);

        public void ReleaseCurrent()
        {
        }

        public ValueTask CompleteCurrentAsync(
            InboxCompletion completion = null,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask FailCurrentAsync(
            Exception exception,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask FailCurrentAsync(
            InboxFailure failure,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask RetryCurrentAsync(
            InboxFailure failure,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask<InboxEntry> GetAsync(
            Ulid entryId,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new InboxEntry { Id = entryId });
    }
}
