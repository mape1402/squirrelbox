namespace SquirrelBox.AspNetCore;

/// <summary>
/// Marks an ASP.NET Core endpoint whose SquirrelBox idempotency payload comes from a bound action argument.
/// </summary>
public interface ISquirrelBoxPayloadMetadata
{
    /// <summary>
    /// Gets the endpoint argument name that contains the payload used for idempotency hashing.
    /// </summary>
    string ArgumentName { get; }

    /// <summary>
    /// Gets the named inbox policy selected by the entrypoint.
    /// </summary>
    string PolicyName { get; }

    /// <summary>
    /// Gets the entry lifetime override selected by the entrypoint.
    /// </summary>
    TimeSpan? EntryLifetime { get; }

    /// <summary>
    /// Gets how completed entries opened by the entrypoint block duplicates.
    /// </summary>
    InboxCompletedLockMode CompletedLock { get; }

    /// <summary>
    /// Gets an optional execution mode override selected by the entrypoint.
    /// </summary>
    InboxExecutionMode? ExecutionMode { get; }

    /// <summary>
    /// Gets optional deferred execution settings selected by the entrypoint.
    /// </summary>
    InboxDeferredPolicyOptions Deferred { get; }
}
