namespace SquirrelBox;

/// <summary>
/// Defines the lifecycle state of a SquirrelBox outbox publication attempt.
/// </summary>
public enum OutboxAttemptState
{
    /// <summary>
    /// The envelope is currently being published.
    /// </summary>
    Publishing,

    /// <summary>
    /// The envelope was published successfully.
    /// </summary>
    Published,

    /// <summary>
    /// The publish attempt failed.
    /// </summary>
    Failed,

    /// <summary>
    /// The envelope was discarded.
    /// </summary>
    Discarded
}
