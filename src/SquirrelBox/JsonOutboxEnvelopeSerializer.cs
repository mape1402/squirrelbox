using System.Text.Json;

namespace SquirrelBox;

/// <summary>
/// JSON implementation of <see cref="IOutboxEnvelopeSerializer"/>.
/// </summary>
public sealed class JsonOutboxEnvelopeSerializer : IOutboxEnvelopeSerializer
{
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public string ContentType => "application/json";

    /// <inheritdoc />
    public JsonSerializerOptions JsonOptions => _jsonOptions;

    /// <inheritdoc />
    public byte[] Serialize(object payload, Type payloadType)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(payloadType);

        return JsonSerializer.SerializeToUtf8Bytes(payload, payloadType, _jsonOptions);
    }

    /// <inheritdoc />
    public object Deserialize(byte[] payload, Type payloadType)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(payloadType);

        return JsonSerializer.Deserialize(payload, payloadType, _jsonOptions);
    }
}
