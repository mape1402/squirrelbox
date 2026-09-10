namespace SquirrelBox;

/// <summary>
/// Configures core outbox behavior.
/// </summary>
public sealed class OutboxOptions
{
    /// <summary>
    /// Gets or sets the default transport name used when enqueue requests do not provide one.
    /// </summary>
    public string DefaultTransport { get; set; } = "default";

    /// <summary>
    /// Gets or sets the retry delay used to compute the next attempt timestamp after failures.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets whether missing deferred scheduler registrations should fail enqueue operations.
    /// </summary>
    public bool RequireDeferredScheduler { get; set; }
}
