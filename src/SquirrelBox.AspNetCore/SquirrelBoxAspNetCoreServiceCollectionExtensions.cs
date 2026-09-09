using Microsoft.Extensions.DependencyInjection;

namespace SquirrelBox.AspNetCore;

/// <summary>
/// Provides service registration for SquirrelBox ASP.NET Core integration.
/// </summary>
public static class SquirrelBoxAspNetCoreServiceCollectionExtensions
{
    /// <summary>
    /// Adds SquirrelBox ASP.NET Core options to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional middleware configuration.</param>
    /// <returns>The same service collection for fluent configuration.</returns>
    public static IServiceCollection AddSquirrelBoxAspNetCore(
        this IServiceCollection services,
        Action<SquirrelBoxAspNetCoreOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is null)
            services.AddOptions<SquirrelBoxAspNetCoreOptions>();
        else
            services.Configure(configure);

        return services;
    }
}
