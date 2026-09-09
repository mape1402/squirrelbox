using Microsoft.AspNetCore.Http;

namespace SquirrelBox.AspNetCore;

/// <summary>
/// Configures the SquirrelBox ASP.NET Core middleware.
/// </summary>
public sealed class SquirrelBoxAspNetCoreOptions
{
    /// <summary>
    /// Gets the request header names inspected for explicit idempotency keys.
    /// </summary>
    public IList<string> RequestHeaderNames { get; } = ["Idempotency-Key", "X-Idempotency-Key"];

    /// <summary>
    /// Gets or sets the response header name used to return explicit or computed idempotency keys.
    /// </summary>
    public string ResponseHeaderName { get; set; }

    /// <summary>
    /// Gets the HTTP methods protected by the middleware.
    /// </summary>
    public ISet<string> ProtectedMethods { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    /// <summary>
    /// Gets or sets whether requests without an explicit key can continue so application code can compute one from a bound payload.
    /// </summary>
    public bool AllowApplicationComputedKeys { get; set; } = true;

    /// <summary>
    /// Gets or sets whether completed inline HTTP responses are captured as inbox completion snapshots.
    /// </summary>
    public bool CaptureCompletedResponses { get; set; } = true;

    /// <summary>
    /// Gets or sets whether duplicate completed HTTP requests replay a stored response snapshot when one exists.
    /// </summary>
    public bool ReplayCompletedResponses { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum response body size captured for replay, in bytes.
    /// </summary>
    public long MaxReplayBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Gets the response header names captured and replayed with completed HTTP snapshots.
    /// </summary>
    public ISet<string> CapturedResponseHeaderNames { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Cache-Control",
        "ETag",
        "Location",
        "Retry-After"
    };

    /// <summary>
    /// Gets or sets the source value used for entries opened by the HTTP middleware.
    /// </summary>
    public string Source { get; set; } = "http";

    /// <summary>
    /// Gets or sets the owner value used for entries opened by the HTTP middleware.
    /// </summary>
    public string Owner { get; set; } = "aspnetcore";

    /// <summary>
    /// Gets or sets the operation resolver. The default format is "METHOD /path".
    /// </summary>
    public Func<HttpContext, string> OperationResolver { get; set; }
        = context => $"{context.Request.Method.ToUpperInvariant()} {context.Request.Path.Value}";

    /// <summary>
    /// Gets or sets the execution mode resolver for entries opened by the middleware.
    /// </summary>
    public Func<HttpContext, InboxExecutionMode?> ExecutionModeResolver { get; set; }
        = _ => null;

    /// <summary>
    /// Gets or sets a predicate that determines whether the middleware should protect the current request.
    /// </summary>
    public Func<HttpContext, bool> ShouldHandleRequest { get; set; }
        = _ => true;

    /// <summary>
    /// Gets or sets a predicate that determines whether the middleware should capture the current response for replay.
    /// </summary>
    public Func<HttpContext, bool> ShouldCaptureResponse { get; set; }
        = _ => true;
}
