namespace SquirrelBox;

/// <summary>
/// Represents a live SquirrelBox observability event emitted by inbox, outbox, or deferred work.
/// </summary>
public sealed class SquirrelBoxEvent
{
    /// <summary>
    /// Gets or sets the ULID that identifies this event.
    /// </summary>
    public Ulid Id { get; set; } = Ulid.NewUlid();

    /// <summary>
    /// Gets or sets the event name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the event category.
    /// </summary>
    public string Category { get; set; }

    /// <summary>
    /// Gets or sets the subject id related to the event.
    /// </summary>
    public string SubjectId { get; set; }

    /// <summary>
    /// Gets or sets the correlation id related to the event.
    /// </summary>
    public string CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the status represented by the event.
    /// </summary>
    public string Status { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the event occurred.
    /// </summary>
    public DateTimeOffset OccurredOnUtc { get; set; }

    /// <summary>
    /// Gets the event metadata.
    /// </summary>
    public IDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
