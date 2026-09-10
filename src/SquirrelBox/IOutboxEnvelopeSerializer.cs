using System.Text.Json;

namespace SquirrelBox;

/// <summary>
/// Serializes and deserializes outbox envelope payloads.
/// </summary>
public interface IOutboxEnvelopeSerializer
{
    /// <summary>
    /// Gets the content type produced by the serializer.
    /// </summary>
    string ContentType { get; }

    /// <summary>
    /// Gets the serializer options when JSON serialization is used.
    /// </summary>
    JsonSerializerOptions JsonOptions { get; }

    /// <summary>
    /// Serializes a payload to durable bytes.
    /// </summary>
    /// <param name="payload">The payload to serialize.</param>
    /// <param name="payloadType">The declared payload type.</param>
    /// <returns>The serialized payload bytes.</returns>
    byte[] Serialize(object payload, Type payloadType);

    /// <summary>
    /// Deserializes durable payload bytes to the requested type.
    /// </summary>
    /// <param name="payload">The serialized payload bytes.</param>
    /// <param name="payloadType">The declared payload type.</param>
    /// <returns>The deserialized payload instance.</returns>
    object Deserialize(byte[] payload, Type payloadType);
}
