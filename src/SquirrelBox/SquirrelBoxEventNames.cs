namespace SquirrelBox;

/// <summary>
/// Defines built-in SquirrelBox event names used by diagnostics and dashboard integrations.
/// </summary>
public static class SquirrelBoxEventNames
{
    /// <summary>Indicates that an inbox entry was opened.</summary>
    public const string InboxOpened = "InboxOpened";

    /// <summary>Indicates that an inbox entry was accepted for execution.</summary>
    public const string InboxAccepted = "InboxAccepted";

    /// <summary>Indicates that an inbox entry was detected as duplicate.</summary>
    public const string InboxDuplicated = "InboxDuplicated";

    /// <summary>Indicates that an inbox entry completed.</summary>
    public const string InboxCompleted = "InboxCompleted";

    /// <summary>Indicates that an inbox entry failed.</summary>
    public const string InboxFailed = "InboxFailed";

    /// <summary>Indicates that an outbox envelope was enqueued.</summary>
    public const string OutboxEnqueued = "OutboxEnqueued";

    /// <summary>Indicates that an outbox envelope started publishing.</summary>
    public const string OutboxPublishing = "OutboxPublishing";

    /// <summary>Indicates that an outbox envelope was published.</summary>
    public const string OutboxPublished = "OutboxPublished";

    /// <summary>Indicates that an outbox envelope failed.</summary>
    public const string OutboxFailed = "OutboxFailed";

    /// <summary>Indicates that deferred work was scheduled.</summary>
    public const string DeferredScheduled = "DeferredScheduled";

    /// <summary>Indicates that deferred work started.</summary>
    public const string DeferredStarted = "DeferredStarted";

    /// <summary>Indicates that deferred work completed.</summary>
    public const string DeferredCompleted = "DeferredCompleted";

    /// <summary>Indicates that deferred work failed.</summary>
    public const string DeferredFailed = "DeferredFailed";
}
