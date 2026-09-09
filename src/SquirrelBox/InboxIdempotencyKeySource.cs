namespace SquirrelBox;

/// <summary>
/// Describes how SquirrelBox obtained an idempotency key.
/// </summary>
public enum InboxIdempotencyKeySource
{
    /// <summary>
    /// The caller supplied the key explicitly.
    /// </summary>
    Explicit,

    /// <summary>
    /// SquirrelBox computed the key from the semantic payload hash.
    /// </summary>
    ComputedFromPayload
}
