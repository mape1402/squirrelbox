namespace SquirrelBox;

/// <summary>
/// Defines how an inbox operation selects its effective idempotency key.
/// </summary>
public enum InboxIdempotencyKeyResolutionMode
{
    /// <summary>
    /// Uses the next configured scope, falling back to the global default.
    /// </summary>
    Default,

    /// <summary>
    /// Uses an incoming key when present and otherwise falls back to the configured payload-hash behavior.
    /// </summary>
    PreferIncoming,

    /// <summary>
    /// Ignores any incoming key and always computes the effective key from the payload.
    /// </summary>
    RecomputeFromPayload
}
