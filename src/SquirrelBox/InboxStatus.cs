namespace SquirrelBox;

/// <summary>
/// Represents the persisted status of an inbox entry.
/// </summary>
public enum InboxStatus
{
    /// <summary>
    /// The entry has been reserved and protected work has not completed.
    /// </summary>
    Started,

    /// <summary>
    /// The protected work completed successfully.
    /// </summary>
    Completed,

    /// <summary>
    /// The protected work failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The entry expired before a duplicate was accepted.
    /// </summary>
    Expired
}
