using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SquirrelBox.InMemory;

/// <summary>
/// Provides dependency injection registration for the in-memory SquirrelBox store.
/// </summary>
public static class InMemorySquirrelBoxServiceCollectionExtensions
{
    /// <summary>
    /// Uses the in-memory inbox store.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for fluent registration.</returns>
    public static IServiceCollection UseInMemory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<InMemoryInboxStore>();
        services.AddSingleton<IInboxStore>(provider => provider.GetRequiredService<InMemoryInboxStore>());
        services.AddSingleton<IInboxDiagnosticsStore>(provider => provider.GetRequiredService<InMemoryInboxStore>());
        services.AddSingleton<IOutboxStore, InMemoryOutboxStore>();
        return services;
    }
}
