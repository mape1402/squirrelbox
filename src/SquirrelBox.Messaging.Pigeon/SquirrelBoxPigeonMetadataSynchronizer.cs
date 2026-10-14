using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Pigeon.Messaging;
using Pigeon.Messaging.Contracts;
using Pigeon.Messaging.Producing;
using SquirrelBox.Messaging;

namespace SquirrelBox.Messaging.Pigeon;

internal static class SquirrelBoxPigeonMetadataSynchronizer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static PigeonPublishEnvelope Enrich(
        PigeonPublishEnvelope envelope,
        SquirrelBoxMessageMetadata metadata,
        ISerializer serializer,
        ILogger logger = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(serializer);

        if (metadata?.HasValues != true)
            return envelope;

        var metadataJson = JsonSerializer.Serialize(metadata.Metadata, JsonOptions);
        return Enrich(envelope, metadata.Metadata, metadataJson, serializer, logger);
    }

    public static PigeonPublishEnvelope Enrich(
        PigeonPublishEnvelope envelope,
        string metadataJson,
        ISerializer serializer,
        ILogger logger = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(serializer);

        if (string.IsNullOrWhiteSpace(metadataJson))
            return envelope;

        SquirrelBoxMetadata metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<SquirrelBoxMetadata>(metadataJson, JsonOptions);
        }
        catch (JsonException exception)
        {
            logger?.LogWarning(
                SquirrelBoxLogEvents.MessagingMetadataMissing,
                exception,
                "SquirrelBox Pigeon outbox durable metadata section could not be parsed. Topic={Topic} Version={Version} Operation={Operation}",
                envelope.Topic,
                envelope.Version,
                envelope.Operation);
            return WithEnvelopeMetadata(envelope, metadataJson);
        }

        if (metadata?.HasValues != true)
            return WithEnvelopeMetadata(envelope, metadataJson);

        return Enrich(envelope, metadata, metadataJson, serializer, logger);
    }

    private static PigeonPublishEnvelope Enrich(
        PigeonPublishEnvelope envelope,
        SquirrelBoxMetadata metadata,
        string metadataJson,
        ISerializer serializer,
        ILogger logger)
    {
        var enriched = WithEnvelopeMetadata(envelope, metadataJson);
        return WithWrappedPayloadMetadata(enriched, metadata, serializer, logger);
    }

    private static PigeonPublishEnvelope WithEnvelopeMetadata(
        PigeonPublishEnvelope envelope,
        string metadataJson)
    {
        var publishMetadata = envelope.Metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(envelope.Metadata, StringComparer.OrdinalIgnoreCase);

        publishMetadata[SquirrelBoxMetadataNames.MetadataSection] = metadataJson;
        return Clone(envelope, envelope.Payload, publishMetadata);
    }

    private static PigeonPublishEnvelope WithWrappedPayloadMetadata(
        PigeonPublishEnvelope envelope,
        SquirrelBoxMetadata metadata,
        ISerializer serializer,
        ILogger logger)
    {
        if (envelope.IsRaw || envelope.Payload is not { Length: > 0 })
            return envelope;

        var payloadType = ResolveType(envelope.PayloadType);
        if (!IsWrappedPayload(payloadType))
            return envelope;

        var rawJson = Encoding.UTF8.GetString(envelope.Payload);
        var payload = serializer.Deserialize(rawJson, payloadType);
        if (payload is null)
            return envelope;

        var metadataProperty = payloadType.GetProperty(nameof(WrappedPayload<object>.Metadata))!;
        var wrappedMetadata = ToMutableMetadata(metadataProperty.GetValue(payload));
        if (ContainsStructuredMetadata(wrappedMetadata))
            return envelope;

        wrappedMetadata[SquirrelBoxMetadataNames.MetadataSection] = metadata;
        metadataProperty.SetValue(payload, wrappedMetadata);

        logger?.LogWarning(
            SquirrelBoxLogEvents.PigeonWrappedMetadataRepaired,
            "SquirrelBox injected metadata into Pigeon wrapped payload. Topic={Topic} Version={Version} Operation={Operation} CorrelationId={CorrelationId} TraceId={TraceId}",
            envelope.Topic,
            envelope.Version,
            envelope.Operation,
            envelope.CorrelationId,
            envelope.TraceId);

        return Clone(
            envelope,
            Encoding.UTF8.GetBytes(serializer.Serialize(payload)),
            envelope.Metadata);
    }

    private static Type ResolveType(string typeName)
        => !string.IsNullOrWhiteSpace(typeName) && Type.GetType(typeName) is { } type
            ? type
            : null;

    private static bool IsWrappedPayload(Type type)
        => type?.IsGenericType == true &&
           type.GetGenericTypeDefinition() == typeof(WrappedPayload<>);

    private static Dictionary<string, object> ToMutableMetadata(object metadata)
    {
        if (metadata is IEnumerable<KeyValuePair<string, object>> values)
            return new Dictionary<string, object>(values, StringComparer.OrdinalIgnoreCase);

        return new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
    }

    private static bool ContainsStructuredMetadata(IReadOnlyDictionary<string, object> metadata)
    {
        if (metadata is null ||
            !metadata.TryGetValue(SquirrelBoxMetadataNames.MetadataSection, out var value) ||
            value is null)
        {
            return false;
        }

        return value switch
        {
            SquirrelBoxMetadata structured => structured.HasValues,
            JsonElement { ValueKind: JsonValueKind.Object } => true,
            JsonElement { ValueKind: JsonValueKind.String } => false,
            string => false,
            _ => true
        };
    }

    private static PigeonPublishEnvelope Clone(
        PigeonPublishEnvelope envelope,
        byte[] payload,
        IReadOnlyDictionary<string, string> metadata)
        => new()
        {
            Transport = envelope.Transport,
            Topic = envelope.Topic,
            Version = envelope.Version,
            Operation = envelope.Operation,
            Destination = envelope.Destination,
            Exchange = envelope.Exchange,
            RoutingKey = envelope.RoutingKey,
            ContentType = envelope.ContentType,
            Payload = payload,
            PayloadType = envelope.PayloadType,
            Headers = envelope.Headers is null
                ? null
                : new Dictionary<string, string>(envelope.Headers, StringComparer.OrdinalIgnoreCase),
            Metadata = metadata is null
                ? null
                : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase),
            CorrelationId = envelope.CorrelationId,
            TraceId = envelope.TraceId,
            IsRaw = envelope.IsRaw
        };
}
