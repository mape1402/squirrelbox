using Microsoft.Extensions.Options;
using Pigeon.Messaging;
using Pigeon.Messaging.Contracts;
using Pigeon.Messaging.Producing;

namespace SquirrelBox.Messaging.Pigeon;

/// <summary>
/// Pigeon publish decision interceptor that redirects publish operations to SquirrelBox outbox.
/// </summary>
public sealed class SquirrelBoxPigeonOutboxInterceptor : IPublishDecisionInterceptor
{
    private readonly IOutboxService _outbox;
    private readonly ISerializer _serializer;
    private readonly GlobalSettings _settings;
    private readonly SquirrelBoxPigeonOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxPigeonOutboxInterceptor"/> class.
    /// </summary>
    public SquirrelBoxPigeonOutboxInterceptor(
        IOutboxService outbox,
        ISerializer serializer,
        IOptions<GlobalSettings> settings,
        IOptions<SquirrelBoxPigeonOptions> options)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
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

        var payload = context.IsRaw
            ? CreateRawPayload(context)
            : CreateWrappedPayload(context);

        var envelope = await _outbox.EnqueueAsync(CreateRequest(context, payload), cancellationToken);

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
        PublishContext context,
        SquirrelBoxPigeonOutboxPayload payload)
    {
        var request = new OutboxEnqueueRequest
        {
            Transport = _options.Transport,
            Operation = context.IsRaw ? "pigeon.publish.raw" : "pigeon.publish",
            Destination = ResolveDestination(context.Route),
            Payload = payload,
            PayloadType = typeof(SquirrelBoxPigeonOutboxPayload)
        };

        request.Metadata[SquirrelBoxPigeonMetadataNames.Topic] = payload.Topic;
        request.Metadata[SquirrelBoxPigeonMetadataNames.Exchange] = payload.Exchange;
        request.Metadata[SquirrelBoxPigeonMetadataNames.RoutingKey] = payload.RoutingKey;
        request.Metadata[SquirrelBoxPigeonMetadataNames.PayloadType] = payload.PayloadType;
        request.Metadata[SquirrelBoxPigeonMetadataNames.IsRaw] = payload.IsRaw.ToString();
        request.Metadata[SquirrelBoxPigeonMetadataNames.Version] = context.Version.ToString();

        return request;
    }

    private SquirrelBoxPigeonOutboxPayload CreateRawPayload(PublishContext context)
        => new()
        {
            Payload = _serializer.Serialize(context.Message),
            PayloadType = context.MessageType.AssemblyQualifiedName,
            IsRaw = true,
            Topic = context.Route.Topic,
            Exchange = context.Route.Exchange,
            RoutingKey = context.Route.RoutingKey
        };

    private SquirrelBoxPigeonOutboxPayload CreateWrappedPayload(PublishContext context)
    {
        var wrappedType = typeof(WrappedPayload<>).MakeGenericType(context.MessageType);
        var wrappedPayload = Activator.CreateInstance(wrappedType);

        wrappedType.GetProperty(nameof(WrappedPayload<object>.CreatedOnUtc))!
            .SetValue(wrappedPayload, DateTimeOffset.UtcNow);
        wrappedType.GetProperty(nameof(WrappedPayload<object>.Message))!
            .SetValue(wrappedPayload, context.Message);
        wrappedType.GetProperty(nameof(WrappedPayload<object>.MessageVersion))!
            .SetValue(wrappedPayload, context.Version);
        wrappedType.GetProperty(nameof(WrappedPayload<object>.Metadata))!
            .SetValue(wrappedPayload, SquirrelBoxPigeonPublishContextMetadata.Read(context));
        wrappedType.GetProperty(nameof(WrappedPayload<object>.Domain))!
            .SetValue(wrappedPayload, _settings.Domain);

        return new SquirrelBoxPigeonOutboxPayload
        {
            Payload = _serializer.Serialize(wrappedPayload),
            PayloadType = wrappedType.AssemblyQualifiedName,
            IsRaw = false,
            Topic = context.Route.Topic,
            Exchange = context.Route.Exchange,
            RoutingKey = context.Route.RoutingKey
        };
    }

    private static string ResolveDestination(PublishingRoute route)
    {
        if (!string.IsNullOrWhiteSpace(route.Exchange))
            return $"{route.Exchange}:{route.RoutingKey}";

        return route.Topic;
    }
}
