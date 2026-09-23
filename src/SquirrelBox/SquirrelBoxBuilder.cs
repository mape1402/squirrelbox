using Microsoft.Extensions.DependencyInjection;

namespace SquirrelBox;

internal sealed class SquirrelBoxBuilder : ISquirrelBoxBuilder
{
    public SquirrelBoxBuilder(IServiceCollection services)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
    }

    public IServiceCollection Services { get; }
}
