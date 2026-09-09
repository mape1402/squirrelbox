namespace SquirrelBox;

/// <summary>
/// Describes how a SquirrelBox operation invocation finished.
/// </summary>
public enum SquirrelBoxOperationExecutionState
{
    /// <summary>
    /// The operation executed inline and produced a result.
    /// </summary>
    Executed,

    /// <summary>
    /// The operation was scheduled for deferred execution.
    /// </summary>
    Deferred,

    /// <summary>
    /// The operation was skipped by inbox policy.
    /// </summary>
    Skipped,

    /// <summary>
    /// The operation result was replayed from a completed inbox entry.
    /// </summary>
    Replayed,

    /// <summary>
    /// The operation was rejected by inbox policy.
    /// </summary>
    Rejected
}
