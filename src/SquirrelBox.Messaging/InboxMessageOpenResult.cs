namespace SquirrelBox.Messaging;

/// <summary>
/// Represents the outcome of opening an inbox context for a message.
/// </summary>
/// <param name="OpenResult">The core inbox open result.</param>
/// <param name="Decision">The policy decision for the result.</param>
/// <param name="Metadata">The SquirrelBox metadata that should be propagated to message replies or outgoing messages.</param>
public sealed record InboxMessageOpenResult(
    InboxOpenResult OpenResult,
    InboxDecision Decision,
    SquirrelBoxMessageMetadata Metadata = null)
{
    /// <summary>
    /// Gets a value indicating whether the consumer should execute the message handler.
    /// </summary>
    public bool ShouldExecute => Decision.ShouldExecute;

    /// <summary>
    /// Gets the effective idempotency key that should be propagated to replies.
    /// </summary>
    public string EffectiveIdempotencyKey => Decision.EffectiveIdempotencyKey;

    /// <summary>
    /// Gets the effective correlation id that should be propagated to replies.
    /// </summary>
    public string EffectiveCorrelationId => OpenResult.EffectiveCorrelationId;

    /// <summary>
    /// Gets the effective attempt id that should be propagated to replies.
    /// </summary>
    public string EffectiveAttemptId => OpenResult.EffectiveAttemptId;

    /// <summary>
    /// Gets the effective trace id that should be propagated to replies.
    /// </summary>
    public string EffectiveTraceId => OpenResult.EffectiveTraceId;

    /// <summary>
    /// Gets the effective SquirrelBox messaging metadata.
    /// </summary>
    public SquirrelBoxMessageMetadata EffectiveMetadata => Metadata ?? SquirrelBoxMessageMetadata.Empty;
}
