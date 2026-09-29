namespace SquirrelBox;

/// <summary>
/// Represents one attempt to open or continue an inbox entry.
/// </summary>
public sealed class InboxAttempt
{
    /// <summary>
    /// Gets or sets the attempt record ULID.
    /// </summary>
    public Ulid Id { get; set; }

    /// <summary>
    /// Gets or sets the related inbox entry ULID.
    /// </summary>
    public Ulid InboxEntryId { get; set; }

    /// <summary>
    /// Gets or sets the effective attempt id.
    /// </summary>
    public string AttemptId { get; set; }

    /// <summary>
    /// Gets or sets the metadata name used for the attempt id.
    /// </summary>
    public string AttemptIdName { get; set; }

    /// <summary>
    /// Gets or sets how the attempt id was resolved.
    /// </summary>
    public SquirrelBoxMetadataValueSource AttemptIdSource { get; set; }

    /// <summary>
    /// Gets or sets the trace id for this attempt.
    /// </summary>
    public string TraceId { get; set; }

    /// <summary>
    /// Gets or sets the metadata name used for the trace id.
    /// </summary>
    public string TraceIdName { get; set; }

    /// <summary>
    /// Gets or sets how the trace id was resolved.
    /// </summary>
    public SquirrelBoxMetadataValueSource TraceIdSource { get; set; }

    /// <summary>
    /// Gets or sets the inbox open state observed by this attempt.
    /// </summary>
    public InboxOpenState State { get; set; }

    /// <summary>
    /// Gets or sets when the attempt was observed.
    /// </summary>
    public DateTimeOffset CreatedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets additional attempt metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
