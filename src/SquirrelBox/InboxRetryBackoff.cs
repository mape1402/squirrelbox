namespace SquirrelBox;

/// <summary>
/// Defines retry backoff strategies for deferred inbox execution.
/// </summary>
public enum InboxRetryBackoff
{
    /// <summary>
    /// Uses a fixed retry delay.
    /// </summary>
    Fixed,

    /// <summary>
    /// Increases the retry delay linearly.
    /// </summary>
    Linear,

    /// <summary>
    /// Increases the retry delay exponentially.
    /// </summary>
    Exponential
}
