namespace SquirrelBox;

/// <summary>
/// Represents the outcome of opening or continuing an inbox entry.
/// </summary>
public enum InboxOpenState
{
    /// <summary>
    /// A new entry was opened and protected work may execute.
    /// </summary>
    Opened,

    /// <summary>
    /// An existing ambient context was reused.
    /// </summary>
    Continued,

    /// <summary>
    /// A matching entry is already in progress.
    /// </summary>
    DuplicateInProgress,

    /// <summary>
    /// A matching entry already completed.
    /// </summary>
    DuplicateCompleted,

    /// <summary>
    /// A matching entry previously failed.
    /// </summary>
    DuplicateFailed,

    /// <summary>
    /// The same key was used with a different payload hash.
    /// </summary>
    PayloadConflict,

    /// <summary>
    /// A matching entry has expired.
    /// </summary>
    Expired,

    /// <summary>
    /// No explicit or computed idempotency key was available.
    /// </summary>
    MissingIdempotencyKey
}
