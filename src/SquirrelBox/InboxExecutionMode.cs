namespace SquirrelBox;

/// <summary>
/// Defines how accepted inbox work should be executed.
/// </summary>
public enum InboxExecutionMode
{
    /// <summary>
    /// Execute the protected work in the current flow.
    /// </summary>
    Inline,

    /// <summary>
    /// Persist the work for later execution by a deferred adapter.
    /// </summary>
    Deferred
}
