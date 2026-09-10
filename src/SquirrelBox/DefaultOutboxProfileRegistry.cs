using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SquirrelBox;

/// <summary>
/// Discovers and resolves outbox profiles from configured assemblies.
/// </summary>
public sealed class DefaultOutboxProfileRegistry : IOutboxProfileRegistry
{
    private readonly IOutboxProfile _fallback = new DefaultOutboxProfile();
    private readonly Lazy<IReadOnlyList<IOutboxProfile>> _profiles;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultOutboxProfileRegistry"/> class.
    /// </summary>
    /// <param name="services">The service provider used to activate discovered profiles.</param>
    /// <param name="options">The SquirrelBox options.</param>
    public DefaultOutboxProfileRegistry(IServiceProvider services, IOptions<SquirrelBoxOptions> options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        _profiles = new Lazy<IReadOnlyList<IOutboxProfile>>(
            () => DiscoverProfiles(services, options.Value.OutboxProfileAssemblies));
    }

    /// <inheritdoc />
    public IOutboxProfile Resolve(OutboxEnqueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return _profiles.Value.FirstOrDefault(profile => profile.CanHandle(request)) ?? _fallback;
    }

    private static IReadOnlyList<IOutboxProfile> DiscoverProfiles(
        IServiceProvider services,
        IEnumerable<Assembly> assemblies)
    {
        var profileType = typeof(IOutboxProfile);
        return assemblies
            .Where(assembly => assembly is not null)
            .Distinct()
            .SelectMany(assembly => assembly.DefinedTypes)
            .Where(type => !type.IsAbstract && !type.IsInterface && profileType.IsAssignableFrom(type))
            .Select(type => (IOutboxProfile)ActivatorUtilities.CreateInstance(services, type.AsType()))
            .ToArray();
    }
}
