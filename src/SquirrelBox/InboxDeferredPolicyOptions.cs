namespace SquirrelBox;

/// <summary>
/// Configures deferred inbox execution for an entrypoint policy.
/// </summary>
public sealed class InboxDeferredPolicyOptions
{
    /// <summary>
    /// Gets or sets the durable execution lane.
    /// </summary>
    public string Lane { get; set; }

    /// <summary>
    /// Gets or sets the maximum durable execution attempts.
    /// </summary>
    public int? MaxAttempts { get; set; }

    /// <summary>
    /// Gets or sets the retry delay.
    /// </summary>
    public TimeSpan? Delay { get; set; }

    /// <summary>
    /// Gets or sets the maximum retry delay.
    /// </summary>
    public TimeSpan? MaxDelay { get; set; }

    /// <summary>
    /// Gets or sets the retry backoff strategy.
    /// </summary>
    public InboxRetryBackoff? Backoff { get; set; }

    /// <summary>
    /// Gets or sets the retry jitter ratio.
    /// </summary>
    public double? JitterRatio { get; set; }

    /// <summary>
    /// Gets or sets how long started or retrying work can remain active before it may be reopened.
    /// </summary>
    public TimeSpan? InProgressTimeout { get; set; }

    internal bool HasRetryConfiguration =>
        MaxAttempts is not null ||
        Delay is not null ||
        MaxDelay is not null ||
        Backoff is not null ||
        JitterRatio is not null;

    internal bool HasConfiguration =>
        !string.IsNullOrWhiteSpace(Lane) ||
        HasRetryConfiguration ||
        InProgressTimeout is not null;
}
