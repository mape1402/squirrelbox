using System.ComponentModel.DataAnnotations;

namespace SquirrelBox.EntityFrameworkCore;

/// <summary>
/// Entity Framework Core record used to persist SquirrelBox outbox envelopes.
/// </summary>
public sealed class SquirrelBoxOutboxEnvelopeRecord
{
    /// <summary>
    /// Gets or sets the ULID outbox envelope id stored as text.
    /// </summary>
    [MaxLength(26)]
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the transport that should publish the envelope.
    /// </summary>
    [MaxLength(128)]
    public string Transport { get; set; }

    /// <summary>
    /// Gets or sets the logical outbox operation.
    /// </summary>
    [MaxLength(512)]
    public string Operation { get; set; }

    /// <summary>
    /// Gets or sets the destination for the envelope.
    /// </summary>
    [MaxLength(1024)]
    public string Destination { get; set; }

    /// <summary>
    /// Gets or sets the assembly-qualified CLR type name for the payload.
    /// </summary>
    [MaxLength(1024)]
    public string PayloadType { get; set; }

    /// <summary>
    /// Gets or sets the serialized payload bytes.
    /// </summary>
    public byte[] Payload { get; set; }

    /// <summary>
    /// Gets or sets the payload content type.
    /// </summary>
    [MaxLength(256)]
    public string ContentType { get; set; }

    /// <summary>
    /// Gets or sets the optional correlation id.
    /// </summary>
    [MaxLength(256)]
    public string CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the optional trace id.
    /// </summary>
    [MaxLength(256)]
    public string TraceId { get; set; }

    /// <summary>
    /// Gets or sets the lifecycle status.
    /// </summary>
    [MaxLength(64)]
    public string Status { get; set; }

    /// <summary>
    /// Gets or sets the UTC creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC update timestamp.
    /// </summary>
    public DateTimeOffset UpdatedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC publishing timestamp.
    /// </summary>
    public DateTimeOffset? PublishingOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC published timestamp.
    /// </summary>
    public DateTimeOffset? PublishedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the message can be retried.
    /// </summary>
    public DateTimeOffset? NextAttemptOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the number of failed publication attempts.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Gets or sets the serialized headers.
    /// </summary>
    public string HeadersJson { get; set; }

    /// <summary>
    /// Gets or sets the serialized metadata.
    /// </summary>
    public string MetadataJson { get; set; }

    /// <summary>
    /// Gets or sets the serialized failure details.
    /// </summary>
    public string FailureJson { get; set; }
}
