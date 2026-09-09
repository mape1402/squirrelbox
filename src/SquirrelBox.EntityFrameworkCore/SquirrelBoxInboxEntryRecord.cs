using System.ComponentModel.DataAnnotations;

namespace SquirrelBox.EntityFrameworkCore;

/// <summary>
/// Entity Framework Core record used to persist SquirrelBox inbox entries.
/// </summary>
public sealed class SquirrelBoxInboxEntryRecord
{
    /// <summary>
    /// Gets or sets the ULID inbox entry id stored as text.
    /// </summary>
    [MaxLength(26)]
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the transport or component that opened the inbox entry.
    /// </summary>
    [MaxLength(128)]
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets the protected operation name.
    /// </summary>
    [MaxLength(512)]
    public string Operation { get; set; }

    /// <summary>
    /// Gets or sets the effective idempotency key.
    /// </summary>
    [MaxLength(256)]
    public string IdempotencyKey { get; set; }

    /// <summary>
    /// Gets or sets whether the key was explicit or computed from payload.
    /// </summary>
    [MaxLength(64)]
    public string IdempotencyKeySource { get; set; }

    /// <summary>
    /// Gets or sets the semantic payload hash, when known.
    /// </summary>
    [MaxLength(128)]
    public string PayloadHash { get; set; }

    /// <summary>
    /// Gets or sets the payload type name, when known.
    /// </summary>
    [MaxLength(1024)]
    public string PayloadType { get; set; }

    /// <summary>
    /// Gets or sets the correlation id, when supplied by the caller.
    /// </summary>
    [MaxLength(256)]
    public string CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the inbox status.
    /// </summary>
    [MaxLength(64)]
    public string Status { get; set; }

    /// <summary>
    /// Gets or sets whether this entry should run inline or through a deferred executor.
    /// </summary>
    [MaxLength(64)]
    public string ExecutionMode { get; set; }

    /// <summary>
    /// Gets or sets the UTC creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC update timestamp.
    /// </summary>
    public DateTimeOffset UpdatedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC completion timestamp, when completed.
    /// </summary>
    public DateTimeOffset? CompletedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC expiration timestamp, when configured.
    /// </summary>
    public DateTimeOffset? ExpiresOnUtc { get; set; }

    /// <summary>
    /// Gets or sets a plain failure description for quick queries.
    /// </summary>
    public string Failure { get; set; }

    /// <summary>
    /// Gets or sets serialized completion metadata.
    /// </summary>
    public string CompletionJson { get; set; }

    /// <summary>
    /// Gets or sets serialized failure metadata.
    /// </summary>
    public string FailureDetailsJson { get; set; }

    /// <summary>
    /// Gets or sets serialized inbox metadata.
    /// </summary>
    public string MetadataJson { get; set; }
}
