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

        var responseHeaderName = ResolveResponseHeaderName();
        httpContext.Response.OnStarting(() =>
        {
            if (inbox.LastContext?.EffectiveIdempotencyKey is { Length: > 0 } key)
                httpContext.Response.Headers[responseHeaderName] = key;

            return Task.CompletedTask;
        });

        var key = ResolveRequestKey(httpContext);
        InboxOpenResult openResult = null;

        if (!string.IsNullOrWhiteSpace(key))
        {
            openResult = await inbox.OpenOrContinueAsync(new InboxOpenRequest
            {
                Source = _options.Source,
                Operation = _options.OperationResolver(httpContext),
                IdempotencyKey = key,
                CorrelationId = httpContext.TraceIdentifier,
                Owner = _options.Owner,
                ExecutionMode = _options.ExecutionModeResolver(httpContext)
            }, httpContext.RequestAborted);

            var decision = policyResolver.Resolve(openResult);
            if (decision.Action is not InboxPolicyAction.Continue)
            {
                if (decision.Action is InboxPolicyAction.Replay &&
                    await TryReplayDecisionAsync(httpContext, decision, responseHeaderName))
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
        InboxDecision decision,
        string responseHeaderName)
    {
        if (!_options.ReplayCompletedResponses)
            return false;

        var completion = decision.Entry?.Completion;
        if (completion?.StatusCode is null)
            return false;

        httpContext.Response.StatusCode = completion.StatusCode.Value;

        if (!string.IsNullOrWhiteSpace(completion.ContentType))
            httpContext.Response.ContentType = completion.ContentType;

        if (!string.IsNullOrWhiteSpace(decision.EffectiveIdempotencyKey))
            httpContext.Response.Headers[responseHeaderName] = decision.EffectiveIdempotencyKey;

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
            decision.EffectiveIdempotencyKey
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

    private string ResolveRequestKey(HttpContext httpContext)
        => _options.RequestHeaderNames
            .Select(header => httpContext.Request.Headers[header].FirstOrDefault())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private string ResolveResponseHeaderName()
        => !string.IsNullOrWhiteSpace(_options.ResponseHeaderName)
            ? _options.ResponseHeaderName
            : _options.RequestHeaderNames.FirstOrDefault() ?? "Idempotency-Key";
}
