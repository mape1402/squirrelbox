namespace SquirrelBox;

/// <summary>
/// Represents the deferred execution settings resolved for an inbox entry.
/// </summary>
public sealed class InboxDeferredExecutionOptions
{
    /// <summary>
    /// Gets or sets the policy name that produced these settings.
    /// </summary>
    public string PolicyName { get; set; }

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
    /// Gets or sets how long active work can remain started or retrying before it may be reopened.
    /// </summary>
    public TimeSpan? InProgressTimeout { get; set; }

    /// <summary>
    /// Creates a copy of the current options.
    /// </summary>
    /// <returns>The copied options.</returns>
    public InboxDeferredExecutionOptions Clone()
        => new()
        {
            PolicyName = PolicyName,
            Lane = Lane,
            MaxAttempts = MaxAttempts,
            Delay = Delay,
            MaxDelay = MaxDelay,
            Backoff = Backoff,
            JitterRatio = JitterRatio,
            InProgressTimeout = InProgressTimeout
        };
}
