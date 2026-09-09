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

        if (!_options.ProtectedMethods.Contains(httpContext.Request.Method))
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
                Owner = _options.Owner
            }, httpContext.RequestAborted);

            var decision = policyResolver.Resolve(openResult);
            if (decision.Action is not InboxPolicyAction.Continue)
            {
                await WriteRejectedDecisionAsync(httpContext, decision);
                return;
            }
        }
        else if (!_options.AllowApplicationComputedKeys)
        {
            httpContext.Response.StatusCode = StatusCodes.Status428PreconditionRequired;
            return;
        }

        try
        {
            await _next(httpContext);

            if (openResult?.State == InboxOpenState.Opened && ReferenceEquals(inbox.Current, openResult.Context))
                await inbox.CompleteCurrentAsync(cancellationToken: httpContext.RequestAborted);
        }
        catch (Exception exception)
        {
            if (openResult?.State == InboxOpenState.Opened && ReferenceEquals(inbox.Current, openResult.Context))
                await inbox.FailCurrentAsync(exception, httpContext.RequestAborted);

            throw;
        }
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

    private string ResolveRequestKey(HttpContext httpContext)
        => _options.RequestHeaderNames
            .Select(header => httpContext.Request.Headers[header].FirstOrDefault())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private string ResolveResponseHeaderName()
        => !string.IsNullOrWhiteSpace(_options.ResponseHeaderName)
            ? _options.ResponseHeaderName
            : _options.RequestHeaderNames.FirstOrDefault() ?? "Idempotency-Key";
}
