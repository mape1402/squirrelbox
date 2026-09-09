namespace SquirrelBox;

/// <summary>
/// Configures how a payload type is reduced before hashing.
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
public sealed class InboxFingerprintRuleBuilder<TPayload>
{
    private readonly Action<Func<TPayload, object>> _apply;

    internal InboxFingerprintRuleBuilder(Action<Func<TPayload, object>> apply)
    {
        _apply = apply;
    }

    /// <summary>
    /// Uses the selected projection as the semantic payload fingerprint.
    /// </summary>
    /// <param name="fingerprint">The projection used before hashing.</param>
    public void Use(Func<TPayload, object> fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        _apply(fingerprint);
    }
}
