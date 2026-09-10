namespace SquirrelBox;

/// <summary>
/// Describes a transport-agnostic outbox publish request before it is serialized and persisted.
/// </summary>
public sealed class OutboxEnqueueRequest
{
    /// <summary>
    /// Gets or sets the transport that should deliver the payload.
    /// </summary>
    public string Transport { get; set; }

    /// <summary>
    /// Gets or sets the logical publish operation.
    /// </summary>
    public string Operation { get; set; }

    /// <summary>
    /// Gets or sets the transport destination, such as a topic, exchange, or route.
    /// </summary>
    public string Destination { get; set; }

    /// <summary>
    /// Gets or sets the payload to serialize and publish.
    /// </summary>
    public object Payload { get; set; }

    /// <summary>
    /// Gets or sets the explicit payload type when different from <see cref="Payload"/> runtime type.
    /// </summary>
    public Type PayloadType { get; set; }

    /// <summary>
    /// Gets or sets the payload content type.
    /// </summary>
    public string ContentType { get; set; } = "application/json";

    /// <summary>
    /// Gets the transport headers attached to the envelope.
    /// </summary>
    public IDictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the durable metadata attached to the envelope.
    /// </summary>
    public IDictionary<string, string> Metadata { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the optional correlation id used to group related work.
    /// </summary>
    public string CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the optional trace id used by distributed tracing.
    /// </summary>
    public string TraceId { get; set; }
}
