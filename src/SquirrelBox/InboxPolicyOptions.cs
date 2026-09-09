namespace SquirrelBox;

/// <summary>
/// Defines the neutral actions SquirrelBox should associate with inbox outcomes.
/// </summary>
public sealed class InboxPolicyOptions
{
    /// <summary>
    /// Gets or sets the action used when an entry is opened successfully.
    /// </summary>
    public InboxPolicyAction Opened { get; set; } = InboxPolicyAction.Continue;

    /// <summary>
    /// Gets or sets the action used when the current inbox context is reused.
    /// </summary>
    public InboxPolicyAction Continued { get; set; } = InboxPolicyAction.Continue;

    /// <summary>
    /// Gets or sets the action used when a completed duplicate is detected.
    /// </summary>
    public InboxPolicyAction DuplicateCompleted { get; set; } = InboxPolicyAction.Replay;

    /// <summary>
    /// Gets or sets the action used when an in-progress duplicate is detected.
    /// </summary>
    public InboxPolicyAction DuplicateInProgress { get; set; } = InboxPolicyAction.Skip;

    /// <summary>
    /// Gets or sets the action used when a failed duplicate is detected.
    /// </summary>
    public InboxPolicyAction DuplicateFailed { get; set; } = InboxPolicyAction.Retry;

    /// <summary>
    /// Gets or sets the action used when the same key is reused for a different payload.
    /// </summary>
    public InboxPolicyAction PayloadConflict { get; set; } = InboxPolicyAction.Reject;

    /// <summary>
    /// Gets or sets the action used when an expired entry is detected.
    /// </summary>
    public InboxPolicyAction Expired { get; set; } = InboxPolicyAction.Reject;

    /// <summary>
    /// Gets or sets the action used when no explicit or computed idempotency key is available.
    /// </summary>
    public InboxPolicyAction MissingIdempotencyKey { get; set; } = InboxPolicyAction.Reject;

    /// <summary>
    /// Resolves the configured policy action for an open outcome.
    /// </summary>
    /// <param name="state">The open outcome.</param>
    /// <returns>The configured action for the outcome.</returns>
    public InboxPolicyAction Resolve(InboxOpenState state)
        => state switch
        {
            InboxOpenState.Opened => Opened,
            InboxOpenState.Continued => Continued,
            InboxOpenState.DuplicateCompleted => DuplicateCompleted,
            InboxOpenState.DuplicateInProgress => DuplicateInProgress,
            InboxOpenState.DuplicateFailed => DuplicateFailed,
            InboxOpenState.PayloadConflict => PayloadConflict,
            InboxOpenState.Expired => Expired,
            InboxOpenState.MissingIdempotencyKey => MissingIdempotencyKey,
            _ => InboxPolicyAction.Reject
        };
}
