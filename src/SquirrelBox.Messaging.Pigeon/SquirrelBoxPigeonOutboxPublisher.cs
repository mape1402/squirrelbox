using System.Reflection;
using Microsoft.Extensions.Options;
using Pigeon.Messaging;
using Pigeon.Messaging.Contracts;
using Pigeon.Messaging.Producing;
using Pigeon.Messaging.Producing.Management;

namespace SquirrelBox.Messaging.Pigeon;

/// <summary>
/// SquirrelBox outbox publisher that delivers persisted envelopes through Pigeon.
/// </summary>
public sealed class SquirrelBoxPigeonOutboxPublisher : IOutboxTransportPublisher
{
    private readonly IProducingManager _producingManager;
    private readonly IOutboxEnvelopeSerializer _outboxSerializer;
    private readonly ISerializer _pigeonSerializer;
    private readonly SquirrelBoxPigeonOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxPigeonOutboxPublisher"/> class.
    /// </summary>
    public SquirrelBoxPigeonOutboxPublisher(
        IProducingManager producingManager,
        IOutboxEnvelopeSerializer outboxSerializer,
        ISerializer pigeonSerializer,
        IOptions<SquirrelBoxPigeonOptions> options)
    {
        _producingManager = producingManager ?? throw new ArgumentNullException(nameof(producingManager));
        _outboxSerializer = outboxSerializer ?? throw new ArgumentNullException(nameof(outboxSerializer));
        _pigeonSerializer = pigeonSerializer ?? throw new ArgumentNullException(nameof(pigeonSerializer));
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

        var payload = (SquirrelBoxPigeonOutboxPayload)_outboxSerializer.Deserialize(
            envelope.Payload,
            typeof(SquirrelBoxPigeonOutboxPayload));

        var payloadType = ResolveType(payload.PayloadType);
        var message = _pigeonSerializer.Deserialize(payload.Payload, payloadType);
        var route = !string.IsNullOrWhiteSpace(payload.Exchange)
            ? PublishingRoute.ForExchange(payload.Exchange, payload.RoutingKey)
            : PublishingRoute.ForTopic(payload.Topic);

        if (payload.IsRaw)
        {
            await InvokeGenericPushAsync(nameof(IProducingManager.PushRawAsync), payloadType, message, route, cancellationToken);
            return OutboxPublishResult.Success;
        }

        var messageType = payloadType.GetGenericArguments().Single();
        await InvokeGenericPushAsync(nameof(IProducingManager.PushAsync), messageType, message, route, cancellationToken);
        return OutboxPublishResult.Success;
    }

    private async ValueTask InvokeGenericPushAsync(
        string methodName,
        Type genericType,
        object payload,
        PublishingRoute route,
        CancellationToken cancellationToken)
    {
        var method = typeof(IProducingManager)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(method =>
                method.Name == methodName &&
                method.IsGenericMethodDefinition &&
                method.GetParameters().Length == 3 &&
                method.GetParameters()[1].ParameterType == typeof(PublishingRoute));

        var result = method
            .MakeGenericMethod(genericType)
            .Invoke(_producingManager, new[] { payload, route, cancellationToken });

        await (ValueTask)result;
    }

    private static Type ResolveType(string typeName)
        => !string.IsNullOrWhiteSpace(typeName) && Type.GetType(typeName) is { } type
            ? type
            : throw new InvalidOperationException($"Pigeon outbox payload type '{typeName}' could not be resolved.");
}
