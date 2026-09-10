using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SquirrelBox.EntityFrameworkCore;

/// <summary>
/// Provides dependency injection registration for the SquirrelBox Entity Framework Core store.
/// </summary>
public static class EntityFrameworkSquirrelBoxServiceCollectionExtensions
{
    /// <summary>
    /// Uses an Entity Framework Core <see cref="IDbContextFactory{TContext}"/> as the durable inbox store.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext type that includes the SquirrelBox inbox model.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same service collection for fluent registration.</returns>
    public static IServiceCollection UseEntityFrameworkInbox<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IInboxStore, EntityFrameworkInboxStore<TDbContext>>();
        services.AddScoped<IInboxDiagnosticsStore, EntityFrameworkInboxStore<TDbContext>>();
        return services;
    }

    /// <summary>
    /// Uses an Entity Framework Core <see cref="IDbContextFactory{TContext}"/> as the durable outbox store.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext type that includes the SquirrelBox outbox model.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same service collection for fluent registration.</returns>
    public static IServiceCollection UseEntityFrameworkOutbox<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IOutboxStore, EntityFrameworkOutboxStore<TDbContext>>();
        return services;
    }

    /// <summary>
    /// Uses an Entity Framework Core <see cref="IDbContextFactory{TContext}"/> as the durable inbox and outbox store.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext type that includes the SquirrelBox model.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same service collection for fluent registration.</returns>
    public static IServiceCollection UseEntityFramework<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.UseEntityFrameworkInbox<TDbContext>();
        services.UseEntityFrameworkOutbox<TDbContext>();
        return services;
    }
}
