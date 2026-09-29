using Microsoft.AspNetCore.Http;

namespace SquirrelBox.AspNetCore;

/// <summary>
/// Provides helpers for opening SquirrelBox contexts from ASP.NET Core endpoints.
/// </summary>
public static class SquirrelBoxHttpContextExtensions
{
    /// <summary>
    /// Opens or continues an inbox context using the current HTTP request and a bound payload.
    /// </summary>
    /// <typeparam name="TPayload">The bound payload type.</typeparam>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="inbox">The inbox service.</param>
    /// <param name="payload">The bound payload used to compute the semantic fingerprint when needed.</param>
    /// <param name="operation">Optional operation override.</param>
    /// <param name="source">Optional source override.</param>
    /// <param name="executionMode">Optional execution mode override.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The open result.</returns>
    public static ValueTask<InboxOpenResult> OpenSquirrelBoxAsync<TPayload>(
        this HttpContext httpContext,
        IInboxService inbox,
        TPayload payload,
        string operation = null,
        string source = "http",
        InboxExecutionMode? executionMode = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(inbox);

        var idempotencyKey = ResolveHeader(httpContext, "Idempotency-Key", "X-Idempotency-Key");
        var correlationId = ResolveHeader(httpContext, "Correlation-Id", "X-Correlation-Id");
        var traceId = ResolveHeader(httpContext, "Trace-Id", "X-Trace-Id", "traceparent");

        return inbox.OpenOrContinueAsync(new InboxOpenRequest
        {
            Source = source,
            Operation = operation ?? $"{httpContext.Request.Method.ToUpperInvariant()} {httpContext.Request.Path.Value}",
            IdempotencyKey = idempotencyKey.Value,
            IdempotencyKeyName = idempotencyKey.Name,
            Payload = payload,
            PayloadType = typeof(TPayload).AssemblyQualifiedName,
            CorrelationId = correlationId.Value,
            CorrelationIdName = correlationId.Name,
            TraceId = string.IsNullOrWhiteSpace(traceId.Value) ? httpContext.TraceIdentifier : traceId.Value,
            TraceIdName = traceId.Name,
            AttemptIdName = "SquirrelBox-Attempt-Id",
            Owner = "aspnetcore-endpoint",
            ExecutionMode = executionMode
        }, cancellationToken);
    }

    private static ResolvedHeader ResolveHeader(HttpContext httpContext, params string[] names)
    {
        foreach (var name in names)
        {
            var value = httpContext.Request.Headers[name].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value))
                return new ResolvedHeader(name, value);
        }

        return new ResolvedHeader(names.FirstOrDefault(), null);
    }

    private sealed record ResolvedHeader(string Name, string Value);
}
