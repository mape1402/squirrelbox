namespace SquirrelBox;

/// <summary>
/// Represents the lifecycle state of an outbox envelope.
/// </summary>
public enum OutboxStatus
{
    /// <summary>
    /// The envelope has been persisted and is waiting for deferred publication.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The envelope is currently being delivered by a publisher.
    /// </summary>
    Publishing = 1,

    /// <summary>
    /// The envelope was delivered successfully.
    /// </summary>
    Published = 2,

    /// <summary>
    /// The envelope failed during publication and can be retried by the durable engine.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// The envelope was intentionally discarded and should not be published.
    /// </summary>
    Discarded = 4
}
