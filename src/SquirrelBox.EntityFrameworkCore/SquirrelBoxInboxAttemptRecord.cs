using System.ComponentModel.DataAnnotations;

namespace SquirrelBox.EntityFrameworkCore;

/// <summary>
/// Entity Framework Core record used to persist SquirrelBox inbox attempts.
/// </summary>
public sealed class SquirrelBoxInboxAttemptRecord
{
    /// <summary>
    /// Gets or sets the ULID attempt record id stored as text.
    /// </summary>
    [MaxLength(26)]
    public string Id { get; set; }

    /// <summary>
    /// Gets or sets the related inbox entry id.
    /// </summary>
    [MaxLength(26)]
    public string InboxEntryId { get; set; }

    /// <summary>
    /// Gets or sets the effective attempt id.
    /// </summary>
    [MaxLength(256)]
    public string AttemptId { get; set; }

    /// <summary>
    /// Gets or sets the metadata name used for the attempt id.
    /// </summary>
    [MaxLength(128)]
    public string AttemptIdName { get; set; }

    /// <summary>
    /// Gets or sets how the attempt id was resolved.
    /// </summary>
    [MaxLength(64)]
    public string AttemptIdSource { get; set; }

    /// <summary>
    /// Gets or sets the trace id for this attempt.
    /// </summary>
    [MaxLength(256)]
    public string TraceId { get; set; }

    /// <summary>
    /// Gets or sets the metadata name used for the trace id.
    /// </summary>
    [MaxLength(128)]
    public string TraceIdName { get; set; }

    /// <summary>
    /// Gets or sets how the trace id was resolved.
    /// </summary>
    [MaxLength(64)]
    public string TraceIdSource { get; set; }

    /// <summary>
    /// Gets or sets the inbox open state observed by this attempt.
    /// </summary>
    [MaxLength(64)]
    public string State { get; set; }

    /// <summary>
    /// Gets or sets when this attempt was observed.
    /// </summary>
    public DateTimeOffset CreatedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets serialized attempt metadata.
    /// </summary>
    public string MetadataJson { get; set; }
}
