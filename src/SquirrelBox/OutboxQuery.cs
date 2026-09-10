namespace SquirrelBox;

/// <summary>
/// Describes an outbox diagnostics query.
/// </summary>
public sealed class OutboxQuery
{
    /// <summary>
    /// Gets or sets the optional status filter.
    /// </summary>
    public OutboxStatus? Status { get; set; }

    /// <summary>
    /// Gets or sets the optional transport filter.
    /// </summary>
    public string Transport { get; set; }

    /// <summary>
    /// Gets or sets the optional operation filter.
    /// </summary>
    public string Operation { get; set; }

    /// <summary>
    /// Gets or sets the optional correlation id filter.
    /// </summary>
    public string CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of envelopes to return.
    /// </summary>
    public int Limit { get; set; } = 100;
}
