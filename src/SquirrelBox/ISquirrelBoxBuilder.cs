using Microsoft.Extensions.DependencyInjection;

namespace SquirrelBox;

/// <summary>
/// Represents a SquirrelBox registration pipeline.
/// </summary>
public interface ISquirrelBoxBuilder
{
    /// <summary>
    /// Gets the service collection being configured.
    /// </summary>
    IServiceCollection Services { get; }
}
