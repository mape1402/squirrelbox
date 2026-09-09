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
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The open result.</returns>
    public static ValueTask<InboxOpenResult> OpenSquirrelBoxAsync<TPayload>(
        this HttpContext httpContext,
        IInboxService inbox,
        TPayload payload,
        string operation = null,
        string source = "http",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(inbox);

        var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault()
            ?? httpContext.Request.Headers["X-Idempotency-Key"].FirstOrDefault();

        return inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            source,
            operation ?? $"{httpContext.Request.Method.ToUpperInvariant()} {httpContext.Request.Path.Value}",
            idempotencyKey,
            payload,
            httpContext.TraceIdentifier,
            owner: "aspnetcore-endpoint"), cancellationToken);
    }
}
