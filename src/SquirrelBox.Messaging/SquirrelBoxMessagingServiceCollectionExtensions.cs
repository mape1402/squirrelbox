using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SquirrelBox.Messaging;

/// <summary>
/// Provides service registration for transport-neutral SquirrelBox messaging support.
/// </summary>
public static class SquirrelBoxMessagingServiceCollectionExtensions
{
    /// <summary>
    /// Adds the transport-neutral messaging adapter services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional messaging configuration.</param>
    /// <returns>The same service collection for fluent registration.</returns>
    public static IServiceCollection AddSquirrelBoxMessaging(
        this IServiceCollection services,
        Action<SquirrelBoxMessagingOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is null)
            services.AddOptions<SquirrelBoxMessagingOptions>();
        else
            services.Configure(configure);

        services.TryAddScoped<IInboxMessageService, DefaultInboxMessageService>();
        return services;
    }
}
