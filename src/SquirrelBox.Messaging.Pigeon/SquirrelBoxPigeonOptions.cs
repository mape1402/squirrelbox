using Pigeon.Messaging.Consuming.Dispatching;

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
}
