using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SquirrelBox.InMemory;

/// <summary>
/// Provides dependency injection registration for the in-memory SquirrelBox store.
/// </summary>
public static class InMemorySquirrelBoxServiceCollectionExtensions
{
    /// <summary>
    /// Uses in-memory inbox and outbox storage for SquirrelBox.
    /// </summary>
    /// <param name="builder">The SquirrelBox builder.</param>
    /// <returns>The same SquirrelBox builder for fluent registration.</returns>
    public static ISquirrelBoxBuilder UseInMemory(this ISquirrelBoxBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        AddInMemory(builder.Services);
        return builder;
    }

    private static void AddInMemory(IServiceCollection services)
    {
        services.TryAddSingleton<InMemoryInboxStore>();
        services.AddSingleton<IInboxStore>(provider => provider.GetRequiredService<InMemoryInboxStore>());
        services.AddSingleton<IInboxDiagnosticsStore>(provider => provider.GetRequiredService<InMemoryInboxStore>());
        services.AddSingleton<IOutboxStore, InMemoryOutboxStore>();
    }
}
