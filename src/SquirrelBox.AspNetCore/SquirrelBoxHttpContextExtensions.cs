using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
    /// <param name="configure">Optional entrypoint inbox policy configuration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The open result.</returns>
    public static ValueTask<InboxOpenResult> OpenSquirrelBoxAsync<TPayload>(
        this HttpContext httpContext,
        IInboxService inbox,
        TPayload payload,
        string operation = null,
        string source = null,
        InboxExecutionMode? executionMode = null,
        Action<SquirrelBoxPayloadOptions> configure = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(inbox);

        var options = httpContext.RequestServices
            .GetService<IOptions<SquirrelBoxAspNetCoreOptions>>()?.Value
            ?? new SquirrelBoxAspNetCoreOptions();
        var payloadOptions = new SquirrelBoxPayloadOptions();
        configure?.Invoke(payloadOptions);

        SquirrelBoxHttpInbox.EnsureIdentityHeadersOnStarting(httpContext, inbox, options);

        return inbox.OpenOrContinueAsync(
            SquirrelBoxHttpInbox.CreateOpenRequest(
                httpContext,
                options,
                payload,
                operation,
                source,
                executionMode: executionMode,
                payloadMetadata: new SquirrelBoxPayloadEndpointMetadata(payloadOptions)),
            cancellationToken);
    }
}
