namespace SquirrelBox;

/// <summary>
/// Transport-agnostic payload persisted by the outbox before deferred publication.
/// </summary>
public sealed class OutboxEnvelope
{
    /// <summary>
    /// Gets or sets the ULID that identifies the outbox envelope.
    /// </summary>
    public Ulid Id { get; set; }

    /// <summary>
    /// Gets or sets the transport that should publish the envelope.
    /// </summary>
    public string Transport { get; set; }

    /// <summary>
    /// Gets or sets the logical operation represented by the envelope.
    /// </summary>
    public string Operation { get; set; }

    /// <summary>
    /// Gets or sets the logical destination for the envelope.
    /// </summary>
    public string Destination { get; set; }

    /// <summary>
    /// Gets or sets the assembly-qualified CLR type name for the serialized payload.
    /// </summary>
    public string PayloadType { get; set; }

    /// <summary>
    /// Gets or sets the serialized payload bytes.
    /// </summary>
    public byte[] Payload { get; set; } = [];

    /// <summary>
    /// Gets or sets the payload content type.
    /// </summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>
    /// Gets the transport headers attached to the envelope.
    /// </summary>
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the durable metadata attached to the envelope.
    /// </summary>
    public IDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the optional correlation id used to group related work.
    /// </summary>
    public string CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the optional trace id used by distributed tracing.
    /// </summary>
    public string TraceId { get; set; }

    /// <summary>
    /// Gets or sets the current outbox status.
    /// </summary>
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    /// <summary>
    /// Gets or sets the UTC timestamp when the envelope was created.
    /// </summary>
    public DateTimeOffset CreatedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the envelope was last updated.
    /// </summary>
    public DateTimeOffset UpdatedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when publication started.
    /// </summary>
    public DateTimeOffset? PublishingOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the envelope was published.
    /// </summary>
    public DateTimeOffset? PublishedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when a failed envelope can be retried.
    /// </summary>
    public DateTimeOffset? NextAttemptOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the number of failed publication attempts.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Gets or sets the last failure details, when publication fails.
    /// </summary>
    public OutboxFailure Failure { get; set; }
}
