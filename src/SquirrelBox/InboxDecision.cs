namespace SquirrelBox;

/// <summary>
/// Represents the policy action SquirrelBox resolved for an inbox open result.
/// </summary>
/// <param name="State">The inbox open state.</param>
/// <param name="Action">The neutral action selected by policy.</param>
/// <param name="Entry">The inbox entry related to the decision, when available.</param>
public sealed record InboxDecision(InboxOpenState State, InboxPolicyAction Action, InboxEntry Entry)
{
    /// <summary>
    /// Gets a value indicating whether the caller should execute the protected work.
    /// </summary>
    public bool ShouldExecute => Action is InboxPolicyAction.Continue or InboxPolicyAction.Retry or InboxPolicyAction.Reopen;

    /// <summary>
    /// Gets the effective idempotency key associated with the decision, when available.
    /// </summary>
    public string EffectiveIdempotencyKey => Entry?.IdempotencyKey;
}
