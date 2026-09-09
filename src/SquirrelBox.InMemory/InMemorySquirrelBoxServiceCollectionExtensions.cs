using Microsoft.Extensions.DependencyInjection;

namespace SquirrelBox.InMemory;

public static class InMemorySquirrelBoxServiceCollectionExtensions
{
    public static IServiceCollection UseInMemory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IInboxStore, InMemoryInboxStore>();
        return services;
    }
}
