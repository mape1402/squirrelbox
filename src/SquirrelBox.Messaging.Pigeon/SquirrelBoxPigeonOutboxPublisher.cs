using System.Text;
using Microsoft.Extensions.Options;
using Pigeon.Messaging.Producing;

namespace SquirrelBox.Messaging.Pigeon;

/// <summary>
/// SquirrelBox outbox publisher that delivers persisted envelopes through Pigeon.
/// </summary>
public sealed class SquirrelBoxPigeonOutboxPublisher : IOutboxTransportPublisher
{
    private readonly IPigeonPublisherInvoker _publisherInvoker;
    private readonly IOutboxEnvelopeSerializer _outboxSerializer;
    private readonly SquirrelBoxPigeonOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxPigeonOutboxPublisher"/> class.
    /// </summary>
    public SquirrelBoxPigeonOutboxPublisher(
        IPigeonPublisherInvoker publisherInvoker,
        IOutboxEnvelopeSerializer outboxSerializer,
        IOptions<SquirrelBoxPigeonOptions> options)
    {
        _publisherInvoker = publisherInvoker ?? throw new ArgumentNullException(nameof(publisherInvoker));
        _outboxSerializer = outboxSerializer ?? throw new ArgumentNullException(nameof(outboxSerializer));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public string Transport => _options.Transport;

    /// <inheritdoc />
    public async ValueTask<OutboxPublishResult> PublishAsync(
        OutboxEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var publishEnvelope = DeserializePublishEnvelope(envelope);
        await _publisherInvoker.PublishAsync(publishEnvelope, cancellationToken);
        return OutboxPublishResult.Success;
    }

    private PigeonPublishEnvelope DeserializePublishEnvelope(OutboxEnvelope envelope)
    {
        var payloadType = ResolveType(envelope.PayloadType);

        if (payloadType == typeof(PigeonPublishEnvelope))
        {
            return (PigeonPublishEnvelope)_outboxSerializer.Deserialize(
                envelope.Payload,
                typeof(PigeonPublishEnvelope));
        }

        if (payloadType == typeof(SquirrelBoxPigeonOutboxPayload))
        {
            var legacyPayload = (SquirrelBoxPigeonOutboxPayload)_outboxSerializer.Deserialize(
                envelope.Payload,
                typeof(SquirrelBoxPigeonOutboxPayload));

            return CreateLegacyEnvelope(envelope, legacyPayload);
        }

        throw new InvalidOperationException(
            $"SquirrelBox Pigeon outbox envelope payload type '{envelope.PayloadType}' is not supported.");
    }

    private static Type ResolveType(string typeName)
        => !string.IsNullOrWhiteSpace(typeName) && Type.GetType(typeName) is { } type
            ? type
            : throw new InvalidOperationException($"Pigeon outbox payload type '{typeName}' could not be resolved.");

    private static PigeonPublishEnvelope CreateLegacyEnvelope(
        OutboxEnvelope envelope,
        SquirrelBoxPigeonOutboxPayload payload)
        => new()
        {
            Transport = envelope.Transport,
            Topic = payload.Topic,
            Version = ResolveMetadata(envelope, SquirrelBoxPigeonMetadataNames.Version),
            Operation = envelope.Operation,
            Destination = envelope.Destination,
            Exchange = payload.Exchange,
            RoutingKey = payload.RoutingKey,
            ContentType = envelope.ContentType,
            Payload = Encoding.UTF8.GetBytes(payload.Payload ?? string.Empty),
            PayloadType = payload.PayloadType,
            Headers = new Dictionary<string, string>(envelope.Headers, StringComparer.OrdinalIgnoreCase),
            Metadata = new Dictionary<string, string>(envelope.Metadata, StringComparer.OrdinalIgnoreCase),
            CorrelationId = envelope.CorrelationId,
            TraceId = envelope.TraceId,
            IsRaw = payload.IsRaw
        };

    private static string ResolveMetadata(OutboxEnvelope envelope, string key)
        => envelope.Metadata.TryGetValue(key, out var value)
            ? value
            : null;
}
