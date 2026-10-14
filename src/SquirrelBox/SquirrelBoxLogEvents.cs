using Microsoft.Extensions.Logging;

namespace SquirrelBox;

/// <summary>
/// Defines stable logging event identifiers emitted by SquirrelBox components.
/// </summary>
public static class SquirrelBoxLogEvents
{
    /// <summary>Inbox context was opened or continued.</summary>
    public static readonly EventId InboxOpened = new(1000, nameof(InboxOpened));

    /// <summary>Inbox request was accepted for execution.</summary>
    public static readonly EventId InboxAccepted = new(1001, nameof(InboxAccepted));

    /// <summary>Inbox request was rejected or skipped by policy.</summary>
    public static readonly EventId InboxRejected = new(1002, nameof(InboxRejected));

    /// <summary>Inbox request did not include or derive an idempotency key.</summary>
    public static readonly EventId InboxMissingIdempotencyKey = new(1003, nameof(InboxMissingIdempotencyKey));

    /// <summary>Inbox context completed successfully.</summary>
    public static readonly EventId InboxCompleted = new(1004, nameof(InboxCompleted));

    /// <summary>Inbox context failed.</summary>
    public static readonly EventId InboxFailed = new(1005, nameof(InboxFailed));

    /// <summary>Inbox context entered retry state.</summary>
    public static readonly EventId InboxRetrying = new(1006, nameof(InboxRetrying));

    /// <summary>Outbox envelope was enqueued.</summary>
    public static readonly EventId OutboxEnqueued = new(2000, nameof(OutboxEnqueued));

    /// <summary>Outbox envelope started publishing.</summary>
    public static readonly EventId OutboxPublishing = new(2001, nameof(OutboxPublishing));

    /// <summary>Outbox envelope was published.</summary>
    public static readonly EventId OutboxPublished = new(2002, nameof(OutboxPublished));

    /// <summary>Outbox envelope failed.</summary>
    public static readonly EventId OutboxFailed = new(2003, nameof(OutboxFailed));

    /// <summary>Messaging metadata required by SquirrelBox is missing.</summary>
    public static readonly EventId MessagingMetadataMissing = new(3000, nameof(MessagingMetadataMissing));

    /// <summary>Pigeon wrapped payload metadata was repaired from durable metadata.</summary>
    public static readonly EventId PigeonWrappedMetadataRepaired = new(3001, nameof(PigeonWrappedMetadataRepaired));

    /// <summary>Pigeon publish was captured by SquirrelBox outbox.</summary>
    public static readonly EventId PigeonOutboxIntercepted = new(3002, nameof(PigeonOutboxIntercepted));

    /// <summary>Pigeon outbox envelope was delivered through Pigeon.</summary>
    public static readonly EventId PigeonOutboxPublished = new(3003, nameof(PigeonOutboxPublished));

    /// <summary>Pigeon consumer execution was deferred.</summary>
    public static readonly EventId PigeonConsumerDeferred = new(3004, nameof(PigeonConsumerDeferred));
}
