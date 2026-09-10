namespace SquirrelBox.Messaging.Pigeon;

/// <summary>
/// Durable payload used by SquirrelBox to publish a prepared Pigeon message later.
/// </summary>
public sealed class SquirrelBoxPigeonOutboxPayload
{
    /// <summary>
    /// Gets or sets the serialized final Pigeon payload.
    /// </summary>
    public string Payload { get; set; }

    /// <summary>
    /// Gets or sets the assembly-qualified final payload type.
    /// </summary>
    public string PayloadType { get; set; }

    /// <summary>
    /// Gets or sets whether the payload should be published without the Pigeon wrapper.
    /// </summary>
    public bool IsRaw { get; set; }

    /// <summary>
    /// Gets or sets the logical topic for topic-based publishing.
    /// </summary>
    public string Topic { get; set; }

    /// <summary>
    /// Gets or sets the broker exchange for routed publishing.
    /// </summary>
    public string Exchange { get; set; }

    /// <summary>
    /// Gets or sets the broker routing key for routed publishing.
    /// </summary>
    public string RoutingKey { get; set; }
}
