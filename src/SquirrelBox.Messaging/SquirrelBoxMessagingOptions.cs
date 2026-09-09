namespace SquirrelBox.Messaging;

/// <summary>
/// Configures transport-neutral messaging inbox behavior.
/// </summary>
public sealed class SquirrelBoxMessagingOptions
{
    /// <summary>
    /// Gets metadata names inspected for explicit idempotency keys.
    /// </summary>
    public IList<string> IdempotencyKeyMetadataNames { get; } = ["idempotency-key", "x-idempotency-key"];

    /// <summary>
    /// Gets or sets the metadata name used to attach the effective idempotency key to replies.
    /// </summary>
    public string ReplyIdempotencyKeyMetadataName { get; set; } = "idempotency-key";

    /// <summary>
    /// Gets or sets whether the broker message id can be used as an idempotency key.
    /// </summary>
    public bool UseMessageIdWhenKeyIsMissing { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the correlation id can be used as an idempotency key when no key or message id exists.
    /// </summary>
    public bool UseCorrelationIdWhenKeyIsMissing { get; set; }

    /// <summary>
    /// Gets or sets whether a semantic payload hash can be used when no metadata key exists.
    /// </summary>
    public bool AllowPayloadHashAsIdempotencyKey { get; set; } = true;

    /// <summary>
    /// Gets or sets the operation resolver. The default includes topic, version, subscription, and operation.
    /// </summary>
    public Func<InboxMessageContext, string> OperationResolver { get; set; } = context =>
    {
        var topic = string.IsNullOrWhiteSpace(context.Topic) ? "unknown-topic" : context.Topic;
        var version = string.IsNullOrWhiteSpace(context.Version) ? "unversioned" : context.Version;
        var subscription = string.IsNullOrWhiteSpace(context.Subscription) ? "default" : context.Subscription;
        var operation = string.IsNullOrWhiteSpace(context.Operation) ? "message" : context.Operation;
        return $"{topic}:{version}/{subscription}/{operation}";
    };

    /// <summary>
    /// Gets or sets an optional execution mode resolver for incoming messages.
    /// </summary>
    public Func<InboxMessageContext, InboxExecutionMode?> ExecutionModeResolver { get; set; }
}
