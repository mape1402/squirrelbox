namespace SquirrelBox.Messaging;

/// <summary>
/// Represents the outcome of opening an inbox context for a message.
/// </summary>
/// <param name="OpenResult">The core inbox open result.</param>
/// <param name="Decision">The policy decision for the result.</param>
public sealed record InboxMessageOpenResult(InboxOpenResult OpenResult, InboxDecision Decision)
{
    /// <summary>
    /// Gets a value indicating whether the consumer should execute the message handler.
    /// </summary>
    public bool ShouldExecute => Decision.ShouldExecute;

    /// <summary>
    /// Gets the effective idempotency key that should be propagated to replies.
    /// </summary>
    public string EffectiveIdempotencyKey => Decision.EffectiveIdempotencyKey;
}
