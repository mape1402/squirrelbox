namespace SquirrelBox;

/// <summary>
/// Configures idempotency window and duplicate-lock behavior for an inbox entrypoint.
/// </summary>
public sealed class InboxIdempotencyPolicy
{
    /// <summary>
    /// Gets or sets the policy name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets how long an inbox entry protects an idempotency key.
    /// </summary>
    public TimeSpan? EntryLifetime { get; set; }

    /// <summary>
    /// Gets or sets how completed entries block duplicates.
    /// </summary>
    public InboxCompletedLockMode CompletedLock { get; set; } = InboxCompletedLockMode.UntilExpiration;

    /// <summary>
    /// Gets or sets an optional execution mode override for entries using this policy.
    /// </summary>
    public InboxExecutionMode? ExecutionMode { get; set; }

    /// <summary>
    /// Gets or sets whether payload hashes can be used as idempotency keys for entries using this policy.
    /// </summary>
    public bool? AllowPayloadHashAsIdempotencyKey { get; set; }

    /// <summary>
    /// Gets deferred execution settings for entries using this policy.
    /// </summary>
    public InboxDeferredPolicyOptions Deferred { get; } = new();
}
