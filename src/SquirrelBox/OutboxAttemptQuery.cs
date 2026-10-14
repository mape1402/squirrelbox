namespace SquirrelBox;

/// <summary>
/// Describes an outbox attempt diagnostics query.
/// </summary>
public sealed class OutboxAttemptQuery
{
    /// <summary>
    /// Gets or sets the envelope id to filter by.
    /// </summary>
    public Ulid? OutboxEnvelopeId { get; set; }

    /// <summary>
    /// Gets or sets the state to filter by.
    /// </summary>
    public OutboxAttemptState? State { get; set; }

    /// <summary>
    /// Gets or sets the trace id to filter by.
    /// </summary>
    public string TraceId { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of attempts to return.
    /// </summary>
    public int Limit { get; set; } = 100;
}
