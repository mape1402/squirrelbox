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
}
