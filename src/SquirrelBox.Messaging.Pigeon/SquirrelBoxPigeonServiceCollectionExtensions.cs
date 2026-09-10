using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Pigeon.Messaging.Consuming.Dispatching;
using Pigeon.Messaging.Producing;
using SquirrelBox.Messaging;
using SquirrelBox.Mule;

namespace SquirrelBox.Messaging.Pigeon;

/// <summary>
/// Provides Pigeon registration extensions for SquirrelBox.
/// </summary>
public static class SquirrelBoxPigeonServiceCollectionExtensions
{
    /// <summary>
    /// Adds SquirrelBox to the Pigeon consume decision and execution pipelines.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional Pigeon adapter configuration.</param>
    /// <returns>The same service collection for fluent registration.</returns>
    public static IServiceCollection AddSquirrelBoxPigeon(
        this IServiceCollection services,
        Action<SquirrelBoxPigeonOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSquirrelBoxMessaging();
        services.AddSquirrelBoxMule();

        if (configure is null)
            services.AddOptions<SquirrelBoxPigeonOptions>();
        else
            services.Configure(configure);

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IConsumeDecisionInterceptor, SquirrelBoxPigeonDecisionInterceptor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IConsumeExecutionInterceptor, SquirrelBoxPigeonExecutionInterceptor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IPublishDecisionInterceptor, SquirrelBoxPigeonOutboxInterceptor>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOutboxTransportPublisher, SquirrelBoxPigeonOutboxPublisher>());
        return services;
    }
}
