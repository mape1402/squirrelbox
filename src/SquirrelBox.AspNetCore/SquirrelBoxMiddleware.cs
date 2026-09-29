using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace SquirrelBox.AspNetCore;

/// <summary>
/// ASP.NET Core middleware that opens SquirrelBox inbox contexts from HTTP idempotency headers.
/// </summary>
public sealed class SquirrelBoxMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SquirrelBoxAspNetCoreOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next request delegate.</param>
    /// <param name="options">The middleware options.</param>
    public SquirrelBoxMiddleware(RequestDelegate next, IOptions<SquirrelBoxAspNetCoreOptions> options)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Processes the HTTP request.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="inbox">The inbox service.</param>
    /// <param name="policyResolver">The policy resolver.</param>
    /// <returns>A task that completes when request processing finishes.</returns>
    public async Task InvokeAsync(HttpContext httpContext, IInboxService inbox, IInboxPolicyResolver policyResolver)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(inbox);
        ArgumentNullException.ThrowIfNull(policyResolver);

        if (!_options.ShouldHandleRequest(httpContext) ||
            !_options.ProtectedMethods.Contains(httpContext.Request.Method))
        {
            await _next(httpContext);
            return;
        }

        httpContext.Response.OnStarting(() =>
        {
            if (inbox.LastContext?.Identity is { } identity)
                AttachIdentityHeaders(httpContext, identity);

            return Task.CompletedTask;
        });

        var key = ResolveRequestHeader(httpContext, _options.RequestHeaderNames);
        var correlation = ResolveRequestHeader(httpContext, _options.CorrelationIdHeaderNames);
        var trace = ResolveRequestHeader(httpContext, _options.TraceIdHeaderNames);
        InboxOpenResult openResult = null;

        if (!string.IsNullOrWhiteSpace(key.Value))
        {
            openResult = await inbox.OpenOrContinueAsync(new InboxOpenRequest
            {
                Source = _options.Source,
                Operation = _options.OperationResolver(httpContext),
                IdempotencyKey = key.Value,
                IdempotencyKeyName = key.Name,
                CorrelationId = correlation.Value,
                CorrelationIdName = correlation.Name,
                TraceId = string.IsNullOrWhiteSpace(trace.Value) ? httpContext.TraceIdentifier : trace.Value,
                TraceIdName = trace.Name,
                AttemptIdName = ResolveDefaultName(_options.AttemptIdHeaderNames, SquirrelBoxMetadataNames.AttemptId),
                Owner = _options.Owner,
                ExecutionMode = _options.ExecutionModeResolver(httpContext)
            }, httpContext.RequestAborted);

            var decision = policyResolver.Resolve(openResult);
            if (decision.Action is not InboxPolicyAction.Continue)
            {
                if (decision.Action is InboxPolicyAction.Replay &&
                    await TryReplayDecisionAsync(httpContext, decision))
                {
                    return;
                }

                await WriteRejectedDecisionAsync(httpContext, decision);
                return;
            }
        }
        else if (!_options.AllowApplicationComputedKeys)
        {
            httpContext.Response.StatusCode = StatusCodes.Status428PreconditionRequired;
            return;
        }

        var shouldCaptureResponse = _options.CaptureCompletedResponses &&
                                    _options.ShouldCaptureResponse(httpContext);
        var originalBody = httpContext.Response.Body;
        MemoryStream capturedBody = null;

        try
        {
            if (shouldCaptureResponse)
            {
                capturedBody = new MemoryStream();
                httpContext.Response.Body = capturedBody;
            }

            await _next(httpContext);

            if (ResolveCompletableContext(inbox, openResult) is { } context &&
                context.Entry.ExecutionMode == InboxExecutionMode.Inline)
            {
                await inbox.CompleteCurrentAsync(
                    capturedBody is null ? null : CreateCompletion(httpContext, capturedBody),
                    httpContext.RequestAborted);
            }
        }
        catch (Exception exception)
        {
            if (ResolveCompletableContext(inbox, openResult) is not null)
                await inbox.FailCurrentAsync(exception, httpContext.RequestAborted);

            throw;
        }
        finally
        {
            if (capturedBody is not null)
            {
                httpContext.Response.Body = originalBody;
                capturedBody.Position = 0;
                await capturedBody.CopyToAsync(originalBody, httpContext.RequestAborted);
                await capturedBody.DisposeAsync();
            }
        }
    }

    private async Task<bool> TryReplayDecisionAsync(
        HttpContext httpContext,
        InboxDecision decision)
    {
        if (!_options.ReplayCompletedResponses)
            return false;

        var completion = decision.Entry?.Completion;
        if (completion?.StatusCode is null)
            return false;

        httpContext.Response.StatusCode = completion.StatusCode.Value;

        if (!string.IsNullOrWhiteSpace(completion.ContentType))
            httpContext.Response.ContentType = completion.ContentType;

        if (decision.Entry?.ToIdentity() is { } identity)
            AttachIdentityHeaders(httpContext, identity);

        foreach (var header in completion.Headers)
        {
            if (ShouldReplayHeader(header.Key))
                httpContext.Response.Headers[header.Key] = header.Value;
        }

        if (completion.ResultPayload is { Length: > 0 } payload)
            await httpContext.Response.Body.WriteAsync(payload, httpContext.RequestAborted);

        return true;
    }

    private InboxCompletion CreateCompletion(HttpContext httpContext, MemoryStream capturedBody)
    {
        var completion = new InboxCompletion
        {
            ContentType = httpContext.Response.ContentType,
            StatusCode = httpContext.Response.StatusCode,
            ResultPayload = capturedBody.Length <= _options.MaxReplayBodyBytes
                ? capturedBody.ToArray()
                : null
        };

        foreach (var headerName in _options.CapturedResponseHeaderNames)
        {
            if (httpContext.Response.Headers.TryGetValue(headerName, out var value) &&
                !string.IsNullOrWhiteSpace(value.ToString()))
            {
                completion.Headers[headerName] = value.ToString();
            }
        }

        if (capturedBody.Length > _options.MaxReplayBodyBytes)
            completion.Metadata["squirrelbox:http:body-too-large"] = "true";

        return completion;
    }

    private static Task WriteRejectedDecisionAsync(HttpContext httpContext, InboxDecision decision)
    {
        httpContext.Response.StatusCode = decision.Action switch
        {
            InboxPolicyAction.Skip => StatusCodes.Status409Conflict,
            InboxPolicyAction.Replay => StatusCodes.Status409Conflict,
            InboxPolicyAction.Retry => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status409Conflict
        };

        httpContext.Response.ContentType = "application/json";
        return httpContext.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            decision.State,
            decision.Action,
            decision.EffectiveIdempotencyKey,
            decision.Entry?.CorrelationId,
            AttemptId = decision.Entry?.CurrentAttempt?.AttemptId ?? decision.Entry?.LastAttemptId,
            TraceId = decision.Entry?.CurrentAttempt?.TraceId ?? decision.Entry?.LastTraceId
        }));
    }

    private bool ShouldReplayHeader(string headerName)
        => !string.Equals(headerName, "Content-Length", StringComparison.OrdinalIgnoreCase) &&
           !string.Equals(headerName, "Content-Type", StringComparison.OrdinalIgnoreCase) &&
           _options.CapturedResponseHeaderNames.Contains(headerName);

    private static InboxContext ResolveCompletableContext(IInboxService inbox, InboxOpenResult openResult)
    {
        var context = openResult?.Context ?? inbox.Current;
        return context is { OwnsCompletion: true } && ReferenceEquals(inbox.Current, context)
            ? context
            : null;
    }

    private ResolvedHeader ResolveRequestHeader(HttpContext httpContext, IEnumerable<string> names)
    {
        foreach (var name in names.Where(name => !string.IsNullOrWhiteSpace(name)))
        {
            var value = httpContext.Request.Headers[name].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value))
                return new ResolvedHeader(name, value);
        }

        return new ResolvedHeader(ResolveDefaultName(names, null), null);
    }

    private void AttachIdentityHeaders(HttpContext httpContext, SquirrelBoxIdentity identity)
    {
        AttachHeader(httpContext, ResolveIdempotencyHeaderName(identity), identity.Operation.IdempotencyKey?.Value);
        AttachHeader(httpContext, identity.Operation.CorrelationId?.Name, identity.Operation.CorrelationId?.Value);
        AttachHeader(httpContext, identity.Attempt.AttemptId?.Name, identity.Attempt.AttemptId?.Value);
        AttachHeader(httpContext, identity.Attempt.TraceId?.Name, identity.Attempt.TraceId?.Value);
    }

    private string ResolveIdempotencyHeaderName(SquirrelBoxIdentity identity)
        => !string.IsNullOrWhiteSpace(identity.Operation.IdempotencyKey?.Name)
            ? identity.Operation.IdempotencyKey.Name
            : !string.IsNullOrWhiteSpace(_options.ResponseHeaderName)
                ? _options.ResponseHeaderName
                : ResolveDefaultName(_options.RequestHeaderNames, SquirrelBoxMetadataNames.IdempotencyKey);

    private static void AttachHeader(HttpContext httpContext, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
            httpContext.Response.Headers[name] = value;
    }

    private static string ResolveDefaultName(IEnumerable<string> names, string fallback)
        => names?.FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? fallback;

    private sealed record ResolvedHeader(string Name, string Value);
}
