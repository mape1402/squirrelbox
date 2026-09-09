using Microsoft.AspNetCore.Builder;

namespace SquirrelBox.AspNetCore;

/// <summary>
/// Provides application builder extensions for SquirrelBox.
/// </summary>
public static class SquirrelBoxApplicationBuilderExtensions
{
    /// <summary>
    /// Adds SquirrelBox HTTP idempotency middleware to the application pipeline.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <returns>The same application builder for fluent configuration.</returns>
    public static IApplicationBuilder UseSquirrelBox(this IApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseMiddleware<SquirrelBoxMiddleware>();
    }
}
