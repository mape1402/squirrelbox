using Microsoft.Extensions.Options;

namespace SquirrelBox;

/// <summary>
/// Default payload fingerprinter that applies discovered <see cref="InboxFingerprintProfile"/> rules.
/// </summary>
public sealed class DefaultInboxPayloadFingerprinter : IInboxPayloadFingerprinter
{
    private readonly Lazy<IReadOnlyDictionary<Type, Func<object, object>>> _rules;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultInboxPayloadFingerprinter"/> class.
    /// </summary>
    /// <param name="options">The SquirrelBox options containing profile discovery assemblies.</param>
    public DefaultInboxPayloadFingerprinter(IOptions<SquirrelBoxOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _rules = new Lazy<IReadOnlyDictionary<Type, Func<object, object>>>(() => Discover(options.Value));
    }

    /// <inheritdoc />
    public object CreateFingerprint(object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return _rules.Value.TryGetValue(payload.GetType(), out var rule)
            ? rule(payload)
            : payload;
    }

    private static IReadOnlyDictionary<Type, Func<object, object>> Discover(SquirrelBoxOptions options)
    {
        var builder = new InboxFingerprintProfileBuilder();

        foreach (var assembly in options.FingerprintProfileAssemblies.Distinct())
        {
            foreach (var type in assembly.DefinedTypes)
            {
                if (type.IsAbstract ||
                    !typeof(InboxFingerprintProfile).IsAssignableFrom(type.AsType()) ||
                    Activator.CreateInstance(type.AsType()) is not InboxFingerprintProfile profile)
                {
                    continue;
                }

                profile.Configure(builder);
            }
        }

        return builder.Build();
    }
}
