using Pigeon.Messaging.Consuming.Dispatching;
using Pigeon.Messaging.Contracts;
using Pigeon.Messaging.Producing;

namespace SquirrelBox.Messaging.Pigeon;

/// <summary>
/// Configures how Pigeon consume contexts are mapped to SquirrelBox message contexts.
/// </summary>
public sealed class SquirrelBoxPigeonOptions
{
    /// <summary>
    /// Gets or sets the transport name assigned to inbox entries opened by Pigeon.
    /// </summary>
    public string Transport { get; set; } = "pigeon";

    /// <summary>
    /// Gets or sets the metadata key inspected for a broker message id.
    /// </summary>
    public string MessageIdMetadataName { get; set; } = "message-id";

    /// <summary>
    /// Gets or sets an optional operation resolver for Pigeon consume contexts.
    /// </summary>
    public Func<ConsumeContext, string> OperationResolver { get; set; }

    /// <summary>
    /// Gets or sets an optional message contract version resolver for Pigeon consume contexts.
    /// </summary>
    public Func<ConsumeContext, SemanticVersion?> VersionResolver { get; set; }

    /// <summary>
    /// Gets or sets an optional execution mode resolver for Pigeon consume contexts.
    /// </summary>
    public Func<ConsumeContext, InboxExecutionMode?> ExecutionModeResolver { get; set; }

    /// <summary>
    /// Gets or sets whether Pigeon publish operations should be persisted through SquirrelBox outbox.
    /// </summary>
    public bool EnableOutbox { get; set; }

    /// <summary>
    /// Gets or sets an optional predicate used to decide whether a publish operation should use SquirrelBox outbox.
    /// </summary>
    public Func<PublishContext, bool> OutboxPredicate { get; set; }

    /// <summary>
    /// Gets or sets the decision returned when a matching inbox entry is already in progress.
    /// </summary>
    public PigeonConsumeDecision InProgressDecision { get; set; } = PigeonConsumeDecision.AckAndSkip;

    /// <summary>
    /// Gets or sets the decision returned when SquirrelBox rejects a consumed message.
    /// </summary>
    public PigeonConsumeDecision RejectedDecision { get; set; } = PigeonConsumeDecision.Reject;
}
