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
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.AspNetCore;
using SquirrelBox.InMemory;
using NSubstitute;

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
    public void HasInboxMetadata_detects_mvc_filter_metadata_and_rejects_empty_action_metadata()
    {
        var httpContext = new DefaultHttpContext();
        var action = new ControllerActionDescriptor();
        httpContext.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(action),
            "empty-mvc"));

        Assert.False(Invoke<bool>("HasInboxMetadata", httpContext));

        action.FilterDescriptors = [new FilterDescriptor(new SquirrelBoxInboxAttribute(), 0)];

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

    [Fact]
    public void CreateOpenRequest_resolves_explicit_arguments_and_option_fallbacks()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = "trace-fallback";
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.Path = "/orders";
        httpContext.Request.Headers["Trace-Id"] = "trace-header";
        var options = new SquirrelBoxAspNetCoreOptions
        {
            Source = "http-options",
            Owner = "options-owner",
            ExecutionModeResolver = _ => InboxExecutionMode.Inline
        };
        options.RequestHeaderNames.Clear();
        options.AttemptIdHeaderNames.Clear();
        var payload = new TestPayload("1");

        var request = Invoke<InboxOpenRequest>(
            "CreateOpenRequest",
            httpContext,
            options,
            payload,
            "explicit-operation",
            "explicit-source",
            "explicit-owner",
            InboxExecutionMode.Deferred,
            null);

        Assert.Equal("explicit-source", request.Source);
        Assert.Equal("explicit-operation", request.Operation);
        Assert.Equal("explicit-owner", request.Owner);
        Assert.Null(request.IdempotencyKey);
        Assert.Null(request.IdempotencyKeyName);
        Assert.Equal("trace-header", request.TraceId);
        Assert.Equal("Trace-Id", request.TraceIdName);
        Assert.Equal(SquirrelBoxMetadataNames.AttemptId, request.AttemptIdName);
        Assert.Equal(InboxExecutionMode.Deferred, request.ExecutionMode);
        Assert.Equal(InboxCompletedLockMode.Default, request.CompletedLock);
    }

    [Fact]
    public void CreateOpenRequest_handles_null_payload_and_uses_execution_resolver()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Delete;
        httpContext.Request.Path = "/orders/one";
        var options = new SquirrelBoxAspNetCoreOptions
        {
            ExecutionModeResolver = _ => InboxExecutionMode.Deferred
        };

        var request = Invoke<InboxOpenRequest>(
            "CreateOpenRequest",
            httpContext,
            options,
            null,
            null,
            null,
            null,
            null,
            null);

        Assert.Null(request.PayloadType);
        Assert.Equal(InboxExecutionMode.Deferred, request.ExecutionMode);
        Assert.Equal("DELETE /orders/one", request.Operation);
    }

    [Fact]
    public void ResolveRequestHeader_ignores_blank_names_and_returns_null_fallback_for_empty_sequence()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Key"] = "key";

        var resolved = Invoke<object>(
            "ResolveRequestHeader",
            httpContext,
            new[] { " ", "X-Key" });
        var empty = Invoke<object>(
            "ResolveRequestHeader",
            httpContext,
            Array.Empty<string>());

        Assert.Equal("X-Key", resolved.GetType().GetProperty("Name")?.GetValue(resolved));
        Assert.Equal("key", resolved.GetType().GetProperty("Value")?.GetValue(resolved));
        Assert.Null(empty.GetType().GetProperty("Name")?.GetValue(empty));
        Assert.Null(empty.GetType().GetProperty("Value")?.GetValue(empty));
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
    public async Task WriteRejectedDecisionAsync_handles_missing_entry_identity()
    {
        var httpContext = CreateJsonContext();
        var decision = new InboxDecision(InboxOpenState.MissingIdempotencyKey, InboxPolicyAction.Reject, null);

        await Invoke<Task>("WriteRejectedDecisionAsync", httpContext, decision);

        var body = ReadBody(httpContext);
        Assert.Contains(InboxOpenState.MissingIdempotencyKey.ToString(), body);
        Assert.Contains(InboxPolicyAction.Reject.ToString(), body);
    }

    [Fact]
    public async Task WriteRejectedDecisionAsync_uses_last_attempt_when_current_attempt_is_missing()
    {
        var httpContext = CreateJsonContext();
        var entry = CreateEntry();
        entry.CurrentAttempt = null;
        entry.LastAttemptId = "attempt-last";
        entry.LastTraceId = "trace-last";
        var decision = new InboxDecision(InboxOpenState.DuplicateCompleted, InboxPolicyAction.Replay, entry);

        await Invoke<Task>("WriteRejectedDecisionAsync", httpContext, decision);

        var body = ReadBody(httpContext);
        Assert.Contains("attempt-last", body);
        Assert.Contains("trace-last", body);
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
    public async Task WritePayloadVerificationConflictAsync_handles_missing_entry_identity()
    {
        var httpContext = CreateJsonContext();
        var result = new InboxPayloadVerificationResult(InboxPayloadVerificationState.PayloadConflict, null);

        await Invoke<Task>("WritePayloadVerificationConflictAsync", httpContext, result);

        Assert.Equal(StatusCodes.Status409Conflict, httpContext.Response.StatusCode);
        Assert.Contains(InboxPayloadVerificationState.PayloadConflict.ToString(), ReadBody(httpContext));
    }

    [Fact]
    public async Task WritePayloadVerificationConflictAsync_prefers_current_attempt_identity()
    {
        var httpContext = CreateJsonContext();
        var entry = CreateEntry();
        entry.CurrentAttempt = new InboxAttempt
        {
            AttemptId = "attempt-current",
            TraceId = "trace-current"
        };
        entry.LastAttemptId = "attempt-last";
        entry.LastTraceId = "trace-last";

        await Invoke<Task>(
            "WritePayloadVerificationConflictAsync",
            httpContext,
            new InboxPayloadVerificationResult(InboxPayloadVerificationState.PayloadConflict, entry));

        var body = ReadBody(httpContext);
        Assert.Contains("attempt-current", body);
        Assert.Contains("trace-current", body);
        Assert.DoesNotContain("attempt-last", body);
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
    public async Task TryReplayDecisionAsync_skips_content_headers_and_empty_payload()
    {
        var options = new SquirrelBoxAspNetCoreOptions();
        var entry = CreateEntry();
        entry.Completion = new InboxCompletion
        {
            StatusCode = StatusCodes.Status204NoContent,
            Headers =
            {
                ["Content-Length"] = "123",
                ["Content-Type"] = "text/plain",
                ["ETag"] = "\"ok\"",
                ["X-Uncaptured"] = "ignored"
            },
            ResultPayload = []
        };
        var httpContext = CreateJsonContext();

        var replayed = await Invoke<Task<bool>>(
            "TryReplayDecisionAsync",
            httpContext,
            options,
            new InboxDecision(InboxOpenState.DuplicateCompleted, InboxPolicyAction.Replay, entry));

        Assert.True(replayed);
        Assert.Equal(StatusCodes.Status204NoContent, httpContext.Response.StatusCode);
        Assert.Equal("\"ok\"", httpContext.Response.Headers.ETag);
        Assert.False(httpContext.Response.Headers.ContainsKey("Content-Length"));
        Assert.False(httpContext.Response.Headers.ContainsKey("Content-Type"));
        Assert.False(httpContext.Response.Headers.ContainsKey("X-Uncaptured"));
        Assert.Equal(string.Empty, ReadBody(httpContext));
    }

    [Fact]
    public async Task TryReplayDecisionAsync_handles_decision_without_entry()
    {
        var replayed = await Invoke<Task<bool>>(
            "TryReplayDecisionAsync",
            CreateJsonContext(),
            new SquirrelBoxAspNetCoreOptions(),
            new InboxDecision(InboxOpenState.MissingIdempotencyKey, InboxPolicyAction.Replay, null));

        Assert.False(replayed);
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
    public void AttachIdentityHeaders_uses_request_fallback_and_skips_blank_values()
    {
        var httpContext = new DefaultHttpContext();
        var options = new SquirrelBoxAspNetCoreOptions
        {
            ResponseHeaderName = " "
        };
        var identity = new SquirrelBoxIdentity
        {
            Operation = new SquirrelBoxOperationIdentity
            {
                IdempotencyKey = SquirrelBoxMetadataValue.Create(null, "key", SquirrelBoxMetadataValueSource.Generated),
                CorrelationId = SquirrelBoxMetadataValue.Create("X-Correlation", " ", SquirrelBoxMetadataValueSource.Generated)
            },
            Attempt = new SquirrelBoxAttemptIdentity
            {
                AttemptId = SquirrelBoxMetadataValue.Create(null, "attempt", SquirrelBoxMetadataValueSource.Generated),
                TraceId = SquirrelBoxMetadataValue.Create("X-Trace", null, SquirrelBoxMetadataValueSource.Generated)
            }
        };

        Invoke<object>("AttachIdentityHeaders", httpContext, options, identity);

        Assert.Equal("key", httpContext.Response.Headers["Idempotency-Key"]);
        Assert.False(httpContext.Response.Headers.ContainsKey("X-Correlation"));
        Assert.False(httpContext.Response.Headers.ContainsKey("X-Trace"));
    }

    [Fact]
    public void AttachIdentityHeaders_handles_missing_identity_values_and_constant_key_fallback()
    {
        var httpContext = new DefaultHttpContext();
        var options = new SquirrelBoxAspNetCoreOptions { ResponseHeaderName = " " };
        options.RequestHeaderNames.Clear();
        var identity = new SquirrelBoxIdentity
        {
            Operation = new SquirrelBoxOperationIdentity
            {
                IdempotencyKey = SquirrelBoxMetadataValue.Create(null, "key", SquirrelBoxMetadataValueSource.Generated),
                CorrelationId = null
            },
            Attempt = new SquirrelBoxAttemptIdentity
            {
                AttemptId = null,
                TraceId = null
            }
        };

        Invoke<object>("AttachIdentityHeaders", httpContext, options, identity);

        Assert.Equal("key", httpContext.Response.Headers[SquirrelBoxMetadataNames.IdempotencyKey]);
        Assert.Single(httpContext.Response.Headers);
    }

    [Fact]
    public void AttachIdentityHeaders_prefers_identity_name_and_default_name_accepts_null_sequence()
    {
        var httpContext = new DefaultHttpContext();
        var identity = new SquirrelBoxIdentity
        {
            Operation = new SquirrelBoxOperationIdentity
            {
                IdempotencyKey = SquirrelBoxMetadataValue.Create(
                    "X-Explicit-Key",
                    "key",
                    SquirrelBoxMetadataValueSource.Incoming)
            },
            Attempt = new SquirrelBoxAttemptIdentity()
        };

        Invoke<object>("AttachIdentityHeaders", httpContext, new SquirrelBoxAspNetCoreOptions(), identity);
        Invoke<object>(
            "AttachIdentityHeaders",
            new DefaultHttpContext(),
            new SquirrelBoxAspNetCoreOptions(),
            new SquirrelBoxIdentity
            {
                Operation = new SquirrelBoxOperationIdentity { IdempotencyKey = null },
                Attempt = new SquirrelBoxAttemptIdentity()
            });
        var fallback = (string)HttpInboxType
            .GetMethod("ResolveDefaultName", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { null, "fallback" })!;

        Assert.Equal("key", httpContext.Response.Headers["X-Explicit-Key"]);
        Assert.Equal("fallback", fallback);
    }

    [Fact]
    public async Task OpenSquirrelBoxAsync_uses_default_transport_options_and_applies_configuration()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = provider };
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Request.Path = "/manual";
        var inbox = new FakeInboxService();

        var result = await httpContext.OpenSquirrelBoxAsync(
            inbox,
            new TestPayload("manual"),
            configure: options => options.PolicyName = "manual-policy");

        Assert.Same(inbox.OpenResult, result);
    }

    [Fact]
    public async Task Middleware_completes_inline_context_without_response_capture()
    {
        var inbox = Substitute.For<IInboxService>();
        inbox.Current.Returns(CreateContext("middleware"));
        inbox.CompleteCurrentAsync(null, Arg.Any<CancellationToken>())
            .Returns(ValueTask.CompletedTask);
        var options = new SquirrelBoxAspNetCoreOptions
        {
            CaptureCompletedResponses = false,
            ShouldHandleRequest = _ => true
        };
        var middleware = new SquirrelBoxMiddleware(
            context => context.Response.WriteAsync("ok"),
            Microsoft.Extensions.Options.Options.Create(options));
        var httpContext = CreateJsonContext();

        await middleware.InvokeAsync(httpContext, inbox, Substitute.For<IInboxPolicyResolver>());

        await inbox.Received(1).CompleteCurrentAsync(null, Arg.Any<CancellationToken>());
        Assert.Equal("ok", ReadBody(httpContext));
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

    [Theory]
    [InlineData(true, false, "last-key")]
    [InlineData(false, true, "current-key")]
    [InlineData(false, false, null)]
    public async Task EnsureIdentityHeadersOnStarting_resolves_last_current_or_missing_identity(
        bool hasLast,
        bool hasCurrent,
        string expectedKey)
    {
        var feature = new CapturingResponseFeature();
        var features = new FeatureCollection();
        features.Set<IHttpResponseFeature>(feature);
        var httpContext = new DefaultHttpContext(features);
        var inbox = new FakeInboxService
        {
            LastContext = hasLast ? CreateContext("last-key") : null,
            Current = hasCurrent ? CreateContext("current-key") : null
        };

        Invoke<object>(
            "EnsureIdentityHeadersOnStarting",
            httpContext,
            inbox,
            new SquirrelBoxAspNetCoreOptions());
        await feature.FireStartingAsync();

        if (expectedKey is null)
            Assert.False(httpContext.Response.Headers.ContainsKey("Idempotency-Key"));
        else
            Assert.Equal(expectedKey, httpContext.Response.Headers["Idempotency-Key"]);
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
    public void ResolveCompletableContext_requires_current_owned_reference_and_supports_inbox_fallback()
    {
        var httpContext = new DefaultHttpContext();
        var owned = CreateContext("owned", ownsCompletion: true);
        var unowned = CreateContext("unowned", ownsCompletion: false);
        var other = CreateContext("other", ownsCompletion: true);
        var inbox = new FakeInboxService { Current = owned };

        Assert.Same(owned, Invoke<InboxContext>("ResolveCompletableContext", httpContext, inbox, null));
        Assert.Same(owned, Invoke<InboxContext>(
            "ResolveCompletableContext",
            httpContext,
            inbox,
            new InboxOpenResult(InboxOpenState.Opened, owned)));
        Assert.Null(Invoke<InboxContext>(
            "ResolveCompletableContext",
            httpContext,
            inbox,
            new InboxOpenResult(InboxOpenState.Opened, unowned)));
        Assert.Null(Invoke<InboxContext>(
            "ResolveCompletableContext",
            httpContext,
            inbox,
            new InboxOpenResult(InboxOpenState.Opened, other)));
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
    public void SquirrelBoxInboxAttribute_maps_all_retry_options_and_default_values()
    {
        var defaults = new SquirrelBoxInboxAttribute();
        var configured = new SquirrelBoxInboxAttribute
        {
            RetryDelaySeconds = 1,
            RetryMaxDelaySeconds = 5,
            RetryBackoff = InboxRetryBackoff.Linear,
            RetryJitterRatio = 0.25,
            InProgressTimeoutSeconds = 8
        };

        Assert.Null(defaults.EntryLifetime);
        Assert.Null(defaults.ExecutionMode);
        Assert.Null(defaults.Deferred.Delay);
        Assert.Equal(TimeSpan.FromSeconds(1), configured.Deferred.Delay);
        Assert.Equal(TimeSpan.FromSeconds(5), configured.Deferred.MaxDelay);
        Assert.Equal(InboxRetryBackoff.Linear, configured.Deferred.Backoff);
        Assert.Equal(0.25, configured.Deferred.JitterRatio);
        Assert.Equal(TimeSpan.FromSeconds(8), configured.Deferred.InProgressTimeout);
    }

    [Fact]
    public void Endpoint_metadata_clones_deferred_options_and_accepts_blank_argument_name()
    {
        var metadataType = typeof(SquirrelBoxAspNetCoreOptions).Assembly.GetType(
            "SquirrelBox.AspNetCore.SquirrelBoxInboxEndpointMetadata",
            throwOnError: true)!;
        var configured = new SquirrelBoxInboxEndpointOptions { ArgumentName = " " };
        configured.Deferred.Lane = "lane";
        var instance = Activator.CreateInstance(metadataType, configured)!;

        Assert.Equal("request", metadataType.GetProperty("ArgumentName")!.GetValue(instance));
        var deferred = Assert.IsType<InboxDeferredPolicyOptions>(metadataType.GetProperty("Deferred")!.GetValue(instance));
        Assert.Equal("lane", deferred.Lane);
        Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(metadataType, new object[] { null }));
    }

    [Fact]
    public void Middleware_constructor_rejects_null_dependencies_and_null_option_value()
    {
        var nullValue = NSubstitute.Substitute.For<Microsoft.Extensions.Options.IOptions<SquirrelBoxAspNetCoreOptions>>();
        nullValue.Value.Returns((SquirrelBoxAspNetCoreOptions)null);

        Assert.Throws<ArgumentNullException>(() => new SquirrelBoxMiddleware(null, Microsoft.Extensions.Options.Options.Create(new SquirrelBoxAspNetCoreOptions())));
        Assert.Throws<ArgumentNullException>(() => new SquirrelBoxMiddleware(_ => Task.CompletedTask, null));
        Assert.Throws<ArgumentNullException>(() => new SquirrelBoxMiddleware(_ => Task.CompletedTask, nullValue));
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

    private static InboxContext CreateContext(string key, bool ownsCompletion = true)
    {
        var entry = CreateEntry();
        entry.IdempotencyKey = key;
        entry.CurrentAttempt = new InboxAttempt
        {
            AttemptId = "attempt",
            AttemptIdName = "Attempt-Id",
            TraceId = "trace",
            TraceIdName = "Trace-Id"
        };
        return new InboxContext(entry, "test", ownsCompletion);
    }

    private sealed class CapturingResponseFeature : IHttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _starting = [];

        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public string ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted { get; private set; }

        public void OnStarting(Func<object, Task> callback, object state)
            => _starting.Add((callback, state));

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }

        public async Task FireStartingAsync()
        {
            HasStarted = true;
            for (var index = _starting.Count - 1; index >= 0; index--)
                await _starting[index].Callback(_starting[index].State);
        }
    }

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
