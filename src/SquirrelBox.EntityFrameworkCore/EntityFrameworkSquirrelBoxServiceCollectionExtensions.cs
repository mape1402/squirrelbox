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
    /// <param name="builder">The SquirrelBox builder.</param>
    /// <param name="configure">Optional Entity Framework Core storage configuration.</param>
    /// <returns>The same SquirrelBox builder for fluent registration.</returns>
    public static ISquirrelBoxBuilder UseEntityFrameworkInbox<TDbContext>(
        this ISquirrelBoxBuilder builder,
        Action<SquirrelBoxEntityFrameworkOptionsBuilder> configure = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        AddSquirrelBoxModelToDbContext<TDbContext>(builder.Services, BuildOptions(configure));
        AddEntityFrameworkInbox<TDbContext>(builder.Services);
        return builder;
    }

    /// <summary>
    /// Uses a registered Entity Framework Core <see cref="DbContext"/> as the durable outbox store.
    /// </summary>
    /// <typeparam name="TDbContext">The registered application DbContext type.</typeparam>
    /// <param name="builder">The SquirrelBox builder.</param>
    /// <param name="configure">Optional Entity Framework Core storage configuration.</param>
    /// <returns>The same SquirrelBox builder for fluent registration.</returns>
    public static ISquirrelBoxBuilder UseEntityFrameworkOutbox<TDbContext>(
        this ISquirrelBoxBuilder builder,
        Action<SquirrelBoxEntityFrameworkOptionsBuilder> configure = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        AddSquirrelBoxModelToDbContext<TDbContext>(builder.Services, BuildOptions(configure));
        AddEntityFrameworkOutbox<TDbContext>(builder.Services);
        return builder;
    }

    /// <summary>
    /// Uses a registered Entity Framework Core <see cref="DbContext"/> as the durable inbox and outbox store.
    /// </summary>
    /// <typeparam name="TDbContext">The registered application DbContext type.</typeparam>
    /// <param name="builder">The SquirrelBox builder.</param>
    /// <param name="configure">Optional Entity Framework Core storage configuration.</param>
    /// <returns>The same SquirrelBox builder for fluent registration.</returns>
    public static ISquirrelBoxBuilder UseEntityFramework<TDbContext>(
        this ISquirrelBoxBuilder builder,
        Action<SquirrelBoxEntityFrameworkOptionsBuilder> configure = null)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        AddSquirrelBoxModelToDbContext<TDbContext>(builder.Services, BuildOptions(configure));
        AddEntityFramework<TDbContext>(builder.Services);
        return builder;
    }

    private static void AddEntityFrameworkInbox<TDbContext>(IServiceCollection services)
        where TDbContext : DbContext
    {
        services.TryAddScoped<ISquirrelBoxDbContextFactory<TDbContext>, SquirrelBoxDbContextFactory<TDbContext>>();
        services.AddScoped<EntityFrameworkInboxStore<TDbContext>>(provider =>
            new EntityFrameworkInboxStore<TDbContext>(
                provider.GetRequiredService<ISquirrelBoxDbContextFactory<TDbContext>>()));
        services.AddScoped<IInboxStore>(provider => provider.GetRequiredService<EntityFrameworkInboxStore<TDbContext>>());
        services.AddScoped<IInboxDiagnosticsStore>(provider => provider.GetRequiredService<EntityFrameworkInboxStore<TDbContext>>());
    }

    private static void AddEntityFrameworkOutbox<TDbContext>(IServiceCollection services)
        where TDbContext : DbContext
    {
        services.TryAddScoped<ISquirrelBoxDbContextFactory<TDbContext>, SquirrelBoxDbContextFactory<TDbContext>>();
        services.AddScoped<EntityFrameworkOutboxStore<TDbContext>>(provider =>
            new EntityFrameworkOutboxStore<TDbContext>(
                provider.GetRequiredService<ISquirrelBoxDbContextFactory<TDbContext>>()));
        services.AddScoped<IOutboxStore>(provider => provider.GetRequiredService<EntityFrameworkOutboxStore<TDbContext>>());
    }

    private static void AddEntityFramework<TDbContext>(IServiceCollection services)
        where TDbContext : DbContext
    {
        AddEntityFrameworkInbox<TDbContext>(services);
        AddEntityFrameworkOutbox<TDbContext>(services);
    }

    private static SquirrelBoxEntityFrameworkOptions BuildOptions(
        Action<SquirrelBoxEntityFrameworkOptionsBuilder> configure)
    {
        var options = new SquirrelBoxEntityFrameworkOptions();
        configure?.Invoke(new SquirrelBoxEntityFrameworkOptionsBuilder(options));
        return options;
    }

    private static IServiceCollection AddSquirrelBoxModelToDbContext<TDbContext>(
        this IServiceCollection services,
        SquirrelBoxEntityFrameworkOptions squirrelBoxOptions)
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
                var dbContextOptions = ResolveOptions<TDbContext>(descriptor, provider);
                return new DbContextOptionsBuilder<TDbContext>(dbContextOptions)
                    .UseSquirrelBoxModel(squirrelBoxOptions)
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
