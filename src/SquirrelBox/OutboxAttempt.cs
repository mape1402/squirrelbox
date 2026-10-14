namespace SquirrelBox;

/// <summary>
/// Represents a persisted outbox publication attempt.
/// </summary>
public sealed class OutboxAttempt
{
    /// <summary>
    /// Gets or sets the ULID that identifies this outbox attempt.
    /// </summary>
    public Ulid Id { get; set; }

    /// <summary>
    /// Gets or sets the related outbox envelope id.
    /// </summary>
    public Ulid OutboxEnvelopeId { get; set; }

    /// <summary>
    /// Gets or sets the effective attempt id.
    /// </summary>
    public string AttemptId { get; set; }

    /// <summary>
    /// Gets or sets the trace id associated with this attempt.
    /// </summary>
    public string TraceId { get; set; }

    /// <summary>
    /// Gets or sets the attempt state.
    /// </summary>
    public OutboxAttemptState State { get; set; }

    /// <summary>
    /// Gets or sets when the attempt started.
    /// </summary>
    public DateTimeOffset StartedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets when the attempt completed.
    /// </summary>
    public DateTimeOffset? CompletedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets failure details when the attempt failed.
    /// </summary>
    public OutboxFailure Failure { get; set; }

    /// <summary>
    /// Gets or sets additional attempt metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
