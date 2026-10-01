namespace SquirrelBox;

/// <summary>
/// Defines how completed inbox entries keep blocking duplicate work.
/// </summary>
public enum InboxCompletedLockMode
{
    /// <summary>
    /// Uses the completed lock mode from the resolved inbox policy.
    /// </summary>
    Default,

    /// <summary>
    /// Blocks completed duplicates only until the entry expiration window ends.
    /// </summary>
    UntilExpiration,

    /// <summary>
    /// Blocks completed duplicates forever, even after the entry expiration window ends.
    /// </summary>
    Forever
}
