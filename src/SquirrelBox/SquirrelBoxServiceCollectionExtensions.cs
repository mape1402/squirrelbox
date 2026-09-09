using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SquirrelBox;

public static class SquirrelBoxServiceCollectionExtensions
{
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
        services.AddSingleton<IInboxPayloadHasher, JsonInboxPayloadHasher>();
        services.AddScoped<IInbox, DefaultInbox>();

        return services;
    }
}
