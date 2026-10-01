using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Mule;

namespace SquirrelBox.Mule;

/// <summary>
/// Provides Mule registration extensions for SquirrelBox.
/// </summary>
public static class SquirrelBoxMuleServiceCollectionExtensions
{
    /// <summary>
    /// Adds the SquirrelBox Mule deferred scheduler.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for fluent registration.</returns>
    public static IServiceCollection AddSquirrelBoxMule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<MuleSettings>();
        services.TryAddScoped<IInboxMuleScheduler, InboxMuleScheduler>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOutboxDeferredScheduler, SquirrelBoxOutboxMuleScheduler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISquirrelBoxOperationDeferredScheduler, SquirrelBoxOperationMuleScheduler>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<MuleSettings>, SquirrelBoxMuleSettingsConfigurator>());
        return services;
    }
}
