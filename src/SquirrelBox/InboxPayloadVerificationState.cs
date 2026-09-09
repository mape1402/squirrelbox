namespace SquirrelBox;

/// <summary>
/// Represents the outcome of verifying a payload against the current inbox entry.
/// </summary>
public enum InboxPayloadVerificationState
{
    /// <summary>
    /// The payload hash matched the stored hash.
    /// </summary>
    Verified,

    /// <summary>
    /// The payload hash was attached to an entry that did not have one.
    /// </summary>
    Attached,

    /// <summary>
    /// The payload hash differs from the stored hash.
    /// </summary>
    PayloadConflict,

    /// <summary>
    /// No current inbox context was available.
    /// </summary>
    NoCurrentContext
}
