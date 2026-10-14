using System.ComponentModel.DataAnnotations;

namespace SquirrelBox.EntityFrameworkCore;

/// <summary>
/// Entity Framework Core record used to persist SquirrelBox outbox publication attempts.
/// </summary>
public sealed class SquirrelBoxOutboxAttemptRecord
{
    /// <summary>
    /// Gets or sets the ULID outbox attempt record id.
    /// </summary>
    public Ulid Id { get; set; }

    /// <summary>
    /// Gets or sets the related outbox envelope id.
    /// </summary>
    public Ulid OutboxEnvelopeId { get; set; }

    /// <summary>
    /// Gets or sets the effective attempt id.
    /// </summary>
    [MaxLength(256)]
    public string AttemptId { get; set; }

    /// <summary>
    /// Gets or sets the trace id for this publish attempt.
    /// </summary>
    [MaxLength(256)]
    public string TraceId { get; set; }

    /// <summary>
    /// Gets or sets the attempt state.
    /// </summary>
    [MaxLength(64)]
    public string State { get; set; }

    /// <summary>
    /// Gets or sets when publication started.
    /// </summary>
    public DateTimeOffset StartedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets when publication completed.
    /// </summary>
    public DateTimeOffset? CompletedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets serialized failure details.
    /// </summary>
    public string FailureJson { get; set; }

    /// <summary>
    /// Gets or sets serialized attempt metadata.
    /// </summary>
    public string MetadataJson { get; set; }
}
