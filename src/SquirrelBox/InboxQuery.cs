namespace SquirrelBox;

/// <summary>
/// Describes an inbox diagnostics query.
/// </summary>
public sealed class InboxQuery
{
    /// <summary>
    /// Gets or sets the optional status filter.
    /// </summary>
    public InboxStatus? Status { get; set; }

    /// <summary>
    /// Gets or sets the optional source filter.
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets the optional operation filter.
    /// </summary>
    public string Operation { get; set; }

    /// <summary>
    /// Gets or sets the optional idempotency key filter.
    /// </summary>
    public string IdempotencyKey { get; set; }

    /// <summary>
    /// Gets or sets the optional correlation id filter.
    /// </summary>
    public string CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of inbox entries to return.
    /// </summary>
    public int Limit { get; set; } = 100;
}
