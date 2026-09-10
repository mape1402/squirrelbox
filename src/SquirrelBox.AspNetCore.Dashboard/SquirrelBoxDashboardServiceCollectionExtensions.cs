using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SquirrelBox.AspNetCore.Dashboard;

/// <summary>
/// Provides dependency injection registration for the SquirrelBox dashboard.
/// </summary>
public static class SquirrelBoxDashboardServiceCollectionExtensions
{
    /// <summary>
    /// Adds the SquirrelBox dashboard services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional dashboard configuration.</param>
    /// <returns>The same service collection for fluent registration.</returns>
    public static IServiceCollection AddSquirrelBoxDashboard(
        this IServiceCollection services,
        Action<SquirrelBoxDashboardOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is null)
            services.AddOptions<SquirrelBoxDashboardOptions>();
        else
            services.Configure(configure);

        services.AddHttpContextAccessor();
        services.TryAddScoped<ISquirrelBoxDashboardUserAccessor, SquirrelBoxDashboardUserAccessor>();
        return services;
    }
}
