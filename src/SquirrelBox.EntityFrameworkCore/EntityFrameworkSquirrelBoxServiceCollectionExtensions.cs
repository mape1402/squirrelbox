using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SquirrelBox.EntityFrameworkCore;

/// <summary>
/// Provides dependency injection registration for the SquirrelBox Entity Framework Core store.
/// </summary>
public static class EntityFrameworkSquirrelBoxServiceCollectionExtensions
{
    /// <summary>
    /// Uses a registered Entity Framework Core <see cref="DbContext"/> as the durable inbox store.
    /// </summary>
    /// <typeparam name="TDbContext">The registered application DbContext type.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same service collection for fluent registration.</returns>
    public static IServiceCollection UseEntityFrameworkInbox<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSquirrelBoxDbContextOptions<TDbContext>();
        services.TryAddScoped<ISquirrelBoxDbContextFactory<TDbContext>, SquirrelBoxDbContextFactory<TDbContext>>();
        services.AddScoped<EntityFrameworkInboxStore<TDbContext>>(provider =>
            new EntityFrameworkInboxStore<TDbContext>(
                provider.GetRequiredService<ISquirrelBoxDbContextFactory<TDbContext>>()));
        services.AddScoped<IInboxStore>(provider => provider.GetRequiredService<EntityFrameworkInboxStore<TDbContext>>());
        services.AddScoped<IInboxDiagnosticsStore>(provider => provider.GetRequiredService<EntityFrameworkInboxStore<TDbContext>>());
        return services;
    }

    /// <summary>
    /// Uses a registered Entity Framework Core <see cref="DbContext"/> as the durable outbox store.
    /// </summary>
    /// <typeparam name="TDbContext">The registered application DbContext type.</typeparam>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same service collection for fluent registration.</returns>
    public static IServiceCollection UseEntityFrameworkOutbox<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSquirrelBoxDbContextOptions<TDbContext>();
        services.TryAddScoped<ISquirrelBoxDbContextFactory<TDbContext>, SquirrelBoxDbContextFactory<TDbContext>>();
        services.AddScoped<EntityFrameworkOutboxStore<TDbContext>>(provider =>
            new EntityFrameworkOutboxStore<TDbContext>(
                provider.GetRequiredService<ISquirrelBoxDbContextFactory<TDbContext>>()));
        services.AddScoped<IOutboxStore>(provider => provider.GetRequiredService<EntityFrameworkOutboxStore<TDbContext>>());
        return services;
    }

    /// <summary>
    /// Uses a registered Entity Framework Core <see cref="DbContext"/> as the durable inbox and outbox store.
    /// </summary>
    /// <typeparam name="TDbContext">The registered application DbContext type.</typeparam>
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

    private static IServiceCollection AddSquirrelBoxDbContextOptions<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        var serviceType = typeof(DbContextOptions<TDbContext>);
        var descriptor = services.LastOrDefault(service => service.ServiceType == serviceType);

        if (descriptor is null)
        {
            throw new InvalidOperationException(
                $"DbContext '{typeof(TDbContext).Name}' must be registered before enabling SquirrelBox EF storage.");
        }

        services.Remove(descriptor);
        services.Add(new ServiceDescriptor(
            serviceType,
            provider =>
            {
                var options = ResolveOptions<TDbContext>(descriptor, provider);
                return new DbContextOptionsBuilder<TDbContext>(options)
                    .UseSquirrelBoxModel()
                    .Options;
            },
            descriptor.Lifetime));

        return services;
    }

    private static DbContextOptions<TDbContext> ResolveOptions<TDbContext>(
        ServiceDescriptor descriptor,
        IServiceProvider provider)
        where TDbContext : DbContext
    {
        if (descriptor.ImplementationInstance is DbContextOptions<TDbContext> instance)
            return instance;

        if (descriptor.ImplementationFactory is not null)
            return (DbContextOptions<TDbContext>)descriptor.ImplementationFactory(provider);

        if (descriptor.ImplementationType is not null)
            return (DbContextOptions<TDbContext>)ActivatorUtilities.CreateInstance(provider, descriptor.ImplementationType);

        throw new InvalidOperationException($"Unable to resolve DbContextOptions for '{typeof(TDbContext).Name}'.");
    }
}
