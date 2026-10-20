namespace SquirrelBox.Messaging;

/// <summary>
/// Represents the outcome of opening an inbox context for a message.
/// </summary>
public sealed record InboxMessageOpenResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InboxMessageOpenResult"/> record.
    /// </summary>
    /// <param name="OpenResult">The core inbox open result.</param>
    /// <param name="Decision">The policy decision for the result.</param>
    /// <param name="Metadata">The metadata to propagate to replies or outgoing messages.</param>
    public InboxMessageOpenResult(
        InboxOpenResult OpenResult,
        InboxDecision Decision,
        SquirrelBoxMessageMetadata Metadata = null)
    {
        this.OpenResult = OpenResult;
        this.Decision = Decision;
        this.Metadata = Metadata;
    }

    /// <summary>
    /// Gets the core inbox open result.
    /// </summary>
    public InboxOpenResult OpenResult { get; init; }

    /// <summary>
    /// Gets the policy decision for the result.
    /// </summary>
    public InboxDecision Decision { get; init; }

    /// <summary>
    /// Gets the metadata to propagate to replies or outgoing messages.
    /// </summary>
    public SquirrelBoxMessageMetadata Metadata { get; init; }

    /// <summary>
    /// Gets a disabled result used when no messaging inbox policy applies.
    /// </summary>
    public static InboxMessageOpenResult Disabled { get; } = new(null, null, SquirrelBoxMessageMetadata.Empty);

    /// <summary>
    /// Gets a value indicating whether inbox idempotency was enabled for the message.
    /// </summary>
    public bool Enabled => OpenResult is not null;

    /// <summary>
    /// Gets a value indicating whether the consumer should execute the message handler.
    /// </summary>
    public bool ShouldExecute => !Enabled || Decision.ShouldExecute;

    /// <summary>
    /// Gets the effective idempotency key that should be propagated to replies.
    /// </summary>
    public string EffectiveIdempotencyKey => Decision?.EffectiveIdempotencyKey;

    /// <summary>
    /// Gets the effective correlation id that should be propagated to replies.
    /// </summary>
    public string EffectiveCorrelationId => OpenResult?.EffectiveCorrelationId;

    /// <summary>
    /// Gets the effective attempt id that should be propagated to replies.
    /// </summary>
    public string EffectiveAttemptId => OpenResult?.EffectiveAttemptId;

    /// <summary>
    /// Gets the effective trace id that should be propagated to replies.
    /// </summary>
    public string EffectiveTraceId => OpenResult?.EffectiveTraceId;

    /// <summary>
    /// Gets the effective SquirrelBox messaging metadata.
    /// </summary>
    public SquirrelBoxMessageMetadata EffectiveMetadata => Metadata ?? SquirrelBoxMessageMetadata.Empty;

    /// <summary>
    /// Deconstructs the result into its primary values.
    /// </summary>
    /// <param name="openResult">The core inbox open result.</param>
    /// <param name="decision">The policy decision.</param>
    /// <param name="metadata">The transport metadata.</param>
    public void Deconstruct(
        out InboxOpenResult openResult,
        out InboxDecision decision,
        out SquirrelBoxMessageMetadata metadata)
    {
        openResult = OpenResult;
        decision = Decision;
        metadata = Metadata;
    }
}
