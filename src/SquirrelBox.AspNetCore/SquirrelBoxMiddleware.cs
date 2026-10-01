using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace SquirrelBox.AspNetCore;

/// <summary>
/// ASP.NET Core middleware that completes SquirrelBox inbox contexts opened by explicit HTTP entrypoints.
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

        if (!_options.ShouldHandleRequest(httpContext))
        {
            await _next(httpContext);
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

            if (SquirrelBoxHttpInbox.ResolveCompletableContext(httpContext, inbox, openResult: null) is { } context &&
                context.Entry.ExecutionMode == InboxExecutionMode.Inline)
            {
                await inbox.CompleteCurrentAsync(
                    capturedBody is null ? null : SquirrelBoxHttpInbox.CreateCompletion(httpContext, _options, capturedBody),
                    httpContext.RequestAborted);
            }
        }
        catch (Exception exception)
        {
            if (SquirrelBoxHttpInbox.ResolveCompletableContext(httpContext, inbox, openResult: null) is not null)
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
}
