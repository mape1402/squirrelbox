using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SquirrelBox;

/// <summary>
/// Provides dependency injection registration for the SquirrelBox core services.
/// </summary>
public static class SquirrelBoxServiceCollectionExtensions
{
    /// <summary>
    /// Adds the SquirrelBox core services to the service collection.
    /// </summary>
    public static IServiceCollection AddSquirrelBox(
        this IServiceCollection services,
        Action<SquirrelBoxOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
            services.Configure(configure);
        else
            services.AddOptions<SquirrelBoxOptions>();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IInboxContextAccessor, AsyncLocalInboxContextAccessor>();
        services.TryAddSingleton<IInboxPayloadFingerprinter, DefaultInboxPayloadFingerprinter>();
        services.TryAddSingleton<IInboxTransactionRunner, SuppressAmbientTransactionInboxRunner>();
        services.TryAddSingleton<IInboxPolicyResolver, DefaultInboxPolicyResolver>();
        services.TryAddSingleton<ISquirrelBoxOperationRegistry, DefaultSquirrelBoxOperationRegistry>();
        services.AddSingleton<IInboxPayloadHasher, JsonInboxPayloadHasher>();
        services.AddScoped<IInboxService, DefaultInbox>();
        services.AddScoped<ISquirrelBoxOperationService, DefaultSquirrelBoxOperationService>();

        return services;
    }
}
