namespace SquirrelBox;

/// <summary>
/// Represents the outcome returned by a correlation id resolution strategy.
/// </summary>
public enum CorrelationIdResolutionState
{
    /// <summary>
    /// The strategy does not apply to the current operation.
    /// </summary>
    NotApplicable,

    /// <summary>
    /// The strategy applies but no correlation id could be found.
    /// </summary>
    NotFound,

    /// <summary>
    /// The strategy resolved a correlation id.
    /// </summary>
    Resolved
}
