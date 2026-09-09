namespace SquirrelBox;

/// <summary>
/// Builds fingerprint rules for payload types.
/// </summary>
public sealed class InboxFingerprintProfileBuilder
{
    private readonly Dictionary<Type, Func<object, object>> _rules = [];

    /// <summary>
    /// Starts configuring a fingerprint rule for <typeparamref name="TPayload"/>.
    /// </summary>
    /// <typeparam name="TPayload">The payload type.</typeparam>
    /// <returns>A typed rule builder.</returns>
    public InboxFingerprintRuleBuilder<TPayload> For<TPayload>()
        => new(rule => _rules[typeof(TPayload)] = payload => rule((TPayload)payload));

    internal IReadOnlyDictionary<Type, Func<object, object>> Build() => _rules;
}
