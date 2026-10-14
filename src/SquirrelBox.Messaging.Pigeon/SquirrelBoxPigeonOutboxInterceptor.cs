using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Pigeon.Messaging;
using Pigeon.Messaging.Producing;
using SquirrelBox.Messaging;

namespace SquirrelBox.Messaging.Pigeon;

/// <summary>
/// Pigeon publish decision interceptor that redirects publish operations to SquirrelBox outbox.
/// </summary>
public sealed class SquirrelBoxPigeonOutboxInterceptor : IPublishDecisionInterceptor
{
    private readonly IOutboxService _outbox;
    private readonly IInboxService _inbox;
    private readonly IPigeonPublishEnvelopeFactory _envelopeFactory;
    private readonly ISerializer _serializer;
    private readonly ISquirrelBoxMessageMetadataEnricher _metadataEnricher;
    private readonly ILogger<SquirrelBoxPigeonOutboxInterceptor> _logger;
    private readonly SquirrelBoxPigeonOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxPigeonOutboxInterceptor"/> class.
    /// </summary>
    public SquirrelBoxPigeonOutboxInterceptor(
        IOutboxService outbox,
        IInboxService inbox,
        IPigeonPublishEnvelopeFactory envelopeFactory,
        ISerializer serializer,
        ISquirrelBoxMessageMetadataEnricher metadataEnricher,
        IOptions<SquirrelBoxPigeonOptions> options,
        ILogger<SquirrelBoxPigeonOutboxInterceptor> logger = null)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _metadataEnricher = metadataEnricher ?? throw new ArgumentNullException(nameof(metadataEnricher));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? NullLogger<SquirrelBoxPigeonOutboxInterceptor>.Instance;
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

        var metadata = CreateCurrentMetadata();
        var publishEnvelope = WithSquirrelBoxMetadata(
            await _envelopeFactory.CreateAsync(context, cancellationToken),
            metadata);
        var envelope = await _outbox.EnqueueAsync(CreateRequest(publishEnvelope, metadata), cancellationToken);

        _logger.LogInformation(
            SquirrelBoxLogEvents.PigeonOutboxIntercepted,
            "SquirrelBox captured Pigeon publish in outbox. OutboxEnvelopeId={OutboxEnvelopeId} Topic={Topic} Version={Version} Operation={Operation} Destination={Destination} CorrelationId={CorrelationId} TraceId={TraceId}",
            envelope.Id,
            publishEnvelope.Topic,
            publishEnvelope.Version,
            publishEnvelope.Operation,
            publishEnvelope.Destination,
            publishEnvelope.CorrelationId,
            publishEnvelope.TraceId);

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

    private OutboxEnqueueRequest CreateRequest(
        PigeonPublishEnvelope envelope,
        SquirrelBoxMessageMetadata metadata)
    {
        var request = new OutboxEnqueueRequest
        {
            Transport = _options.Transport,
            Operation = envelope.IsRaw ? "pigeon.publish.raw" : "pigeon.publish",
            Destination = ResolveDestination(envelope),
            Payload = envelope,
            PayloadType = typeof(PigeonPublishEnvelope),
            CorrelationId = string.IsNullOrWhiteSpace(envelope.CorrelationId)
                ? metadata?.CorrelationId
                : envelope.CorrelationId,
            TraceId = string.IsNullOrWhiteSpace(envelope.TraceId)
                ? metadata?.TraceId
                : envelope.TraceId
        };

        Copy(envelope.Headers, request.Headers);
        Copy(envelope.Metadata, request.Metadata);
        metadata?.WriteTo(request.Metadata);

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

    private PigeonPublishEnvelope WithSquirrelBoxMetadata(
        PigeonPublishEnvelope envelope,
        SquirrelBoxMessageMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        return SquirrelBoxPigeonMetadataSynchronizer.Enrich(
            envelope,
            metadata,
            _serializer,
            _logger);
    }

    private SquirrelBoxMessageMetadata CreateCurrentMetadata()
        => _metadataEnricher.Create(_inbox.Current?.Identity ?? _inbox.LastContext?.Identity);
}
