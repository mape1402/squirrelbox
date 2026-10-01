namespace SquirrelBox.AspNetCore;

/// <summary>
/// Configures inbox behavior for an ASP.NET Core payload entrypoint.
/// </summary>
public sealed class SquirrelBoxPayloadOptions
{
    /// <summary>
    /// Gets or sets the endpoint argument name that contains the payload used for idempotency hashing.
    /// </summary>
    public string ArgumentName { get; set; } = "request";

    /// <summary>
    /// Gets or sets the named inbox policy selected by this entrypoint.
    /// </summary>
    public string PolicyName { get; set; }

    /// <summary>
    /// Gets or sets the entry lifetime override selected by this entrypoint.
    /// </summary>
    public TimeSpan? EntryLifetime { get; set; }

    /// <summary>
    /// Gets or sets how completed entries opened by this entrypoint block duplicates.
    /// </summary>
    public InboxCompletedLockMode CompletedLock { get; set; } = InboxCompletedLockMode.Default;

    /// <summary>
    /// Gets or sets an optional execution mode override selected by this entrypoint.
    /// </summary>
    public InboxExecutionMode? ExecutionMode { get; set; }

    /// <summary>
    /// Gets deferred execution settings selected by this entrypoint.
    /// </summary>
    public InboxDeferredPolicyOptions Deferred { get; } = new();
}
