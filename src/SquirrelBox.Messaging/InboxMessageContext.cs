namespace SquirrelBox.Messaging;

/// <summary>
/// Describes an incoming message independently from any concrete messaging transport.
/// </summary>
public sealed class InboxMessageContext
{
    /// <summary>
    /// Gets or sets the transport name.
    /// </summary>
    public string Transport { get; init; } = "messaging";

    /// <summary>
    /// Gets or sets the message topic or exchange.
    /// </summary>
    public string Topic { get; init; }

    /// <summary>
    /// Gets or sets the message operation name.
    /// </summary>
    public string Operation { get; init; }

    /// <summary>
    /// Gets or sets the message contract version.
    /// </summary>
    public string Version { get; init; }

    /// <summary>
    /// Gets or sets the subscription or consumer group name.
    /// </summary>
    public string Subscription { get; init; }

    /// <summary>
    /// Gets or sets the broker message id.
    /// </summary>
    public string MessageId { get; init; }

    /// <summary>
    /// Gets or sets the correlation id.
    /// </summary>
    public string CorrelationId { get; init; }

    /// <summary>
    /// Gets or sets the execution mode requested by the transport adapter.
    /// </summary>
    public InboxExecutionMode? ExecutionMode { get; init; }

    /// <summary>
    /// Gets or sets the deserialized payload.
    /// </summary>
    public object Payload { get; init; }

    /// <summary>
    /// Gets message metadata supplied by the transport.
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
