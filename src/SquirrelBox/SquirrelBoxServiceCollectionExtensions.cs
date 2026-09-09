using Microsoft.Extensions.DependencyInjection;

namespace SquirrelBox;

public static class SquirrelBoxServiceCollectionExtensions
{
    public static IServiceCollection AddSquirrelBox(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IInboxPayloadHasher, JsonInboxPayloadHasher>();
        services.AddScoped<IInbox, DefaultInbox>();

        return services;
    }
}
