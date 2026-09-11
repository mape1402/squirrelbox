using Microsoft.Extensions.Options;
using Pigeon.Messaging.Producing;

namespace SquirrelBox.Messaging.Pigeon;

/// <summary>
/// Pigeon publish decision interceptor that redirects publish operations to SquirrelBox outbox.
/// </summary>
public sealed class SquirrelBoxPigeonOutboxInterceptor : IPublishDecisionInterceptor
{
    private readonly IOutboxService _outbox;
    private readonly IPigeonPublishEnvelopeFactory _envelopeFactory;
    private readonly SquirrelBoxPigeonOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxPigeonOutboxInterceptor"/> class.
    /// </summary>
    public SquirrelBoxPigeonOutboxInterceptor(
        IOutboxService outbox,
        IPigeonPublishEnvelopeFactory envelopeFactory,
        IOptions<SquirrelBoxPigeonOptions> options)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async ValueTask<PigeonPublishDecisionResult> InterceptAsync(
        PublishContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!_options.EnableOutbox ||
            _options.OutboxPredicate is { } predicate && !predicate(context))
        {
            return PigeonPublishDecisionResult.Continue;
        }

        var publishEnvelope = await _envelopeFactory.CreateAsync(context, cancellationToken);
        var envelope = await _outbox.EnqueueAsync(CreateRequest(publishEnvelope), cancellationToken);

        return new PigeonPublishDecisionResult(
            PigeonPublishDecision.Skip,
            "SquirrelBox persisted the Pigeon publish operation in the outbox.")
        {
            Metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [SquirrelBoxPigeonMetadataNames.OutboxEnvelopeId] = envelope.Id.ToString()
            }
        };
    }

    private OutboxEnqueueRequest CreateRequest(PigeonPublishEnvelope envelope)
    {
        var request = new OutboxEnqueueRequest
        {
            Transport = _options.Transport,
            Operation = envelope.IsRaw ? "pigeon.publish.raw" : "pigeon.publish",
            Destination = ResolveDestination(envelope),
            Payload = envelope,
            PayloadType = typeof(PigeonPublishEnvelope),
            CorrelationId = envelope.CorrelationId,
            TraceId = envelope.TraceId
        };

        Copy(envelope.Headers, request.Headers);
        Copy(envelope.Metadata, request.Metadata);

        request.Metadata[SquirrelBoxPigeonMetadataNames.Transport] = envelope.Transport;
        request.Metadata[SquirrelBoxPigeonMetadataNames.Topic] = envelope.Topic;
        request.Metadata[SquirrelBoxPigeonMetadataNames.Exchange] = envelope.Exchange;
        request.Metadata[SquirrelBoxPigeonMetadataNames.RoutingKey] = envelope.RoutingKey;
        request.Metadata[SquirrelBoxPigeonMetadataNames.PayloadType] = envelope.PayloadType;
        request.Metadata[SquirrelBoxPigeonMetadataNames.ContentType] = envelope.ContentType;
        request.Metadata[SquirrelBoxPigeonMetadataNames.IsRaw] = envelope.IsRaw.ToString();
        request.Metadata[SquirrelBoxPigeonMetadataNames.Version] = envelope.Version;
        request.Metadata[SquirrelBoxPigeonMetadataNames.Operation] = envelope.Operation;

        return request;
    }

    private static void Copy(IReadOnlyDictionary<string, string> source, IDictionary<string, string> target)
    {
        if (source is null)
            return;

        foreach (var item in source)
            target[item.Key] = item.Value;
    }

    private static string ResolveDestination(PigeonPublishEnvelope envelope)
    {
        if (!string.IsNullOrWhiteSpace(envelope.Destination))
            return envelope.Destination;

        if (!string.IsNullOrWhiteSpace(envelope.Exchange))
            return $"{envelope.Exchange}:{envelope.RoutingKey}";

        return envelope.Topic;
    }
}
