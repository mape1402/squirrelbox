using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pigeon.Messaging;
using Pigeon.Messaging.Contracts;
using Pigeon.Messaging.Outbox;
using Pigeon.Messaging.Producing;
using Pigeon.Messaging.Producing.Management;
using SquirrelBox.InMemory;
using SquirrelBox.Messaging.Pigeon;

namespace SquirrelBox.Messaging.Tests;

public sealed class SquirrelBoxPigeonOutboxTests
{
    [Fact]
    public async Task PublishDecisionInterceptor_persists_pigeon_publish_in_squirrelbox_outbox()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonOutboxInterceptor>(scope.ServiceProvider);
        var context = CreatePublishContext(isRaw: false);
        context.AddMetadata("tenant", "north");

        var result = await interceptor.InterceptAsync(context);
        var envelopes = await scope.ServiceProvider.GetRequiredService<IOutboxStore>()
            .QueryAsync(new OutboxQuery { Transport = "pigeon" });

        var envelope = Assert.Single(envelopes);
        Assert.Equal(PigeonPublishDecision.Skip, result.Decision);
        Assert.Equal(envelope.Id.ToString(), result.Metadata["squirrelbox-outbox-id"]);
        Assert.Equal("pigeon.publish", envelope.Operation);
        Assert.Equal("orders", envelope.Destination);
        Assert.Equal("1.2.0", envelope.Metadata["squirrelbox-pigeon-version"]);
    }

    [Fact]
    public async Task PigeonOutboxPublisher_rehydrates_wrapped_payload_and_pushes_through_producing_manager()
    {
        using var provider = CreateProvider();
        var outboxSerializer = provider.GetRequiredService<IOutboxEnvelopeSerializer>();
        var pigeonSerializer = provider.GetRequiredService<ISerializer>();
        var payload = new WrappedPayload<OrderMessage>
        {
            Domain = "sales",
            CreatedOnUtc = DateTimeOffset.UtcNow,
            MessageVersion = SemanticVersion.Parse("1.2.0"),
            Message = new OrderMessage("order-1"),
            Metadata = new Dictionary<string, object> { ["tenant"] = "north" }
        };

        var outboxPayload = new SquirrelBoxPigeonOutboxPayload
        {
            Payload = pigeonSerializer.Serialize(payload),
            PayloadType = typeof(WrappedPayload<OrderMessage>).AssemblyQualifiedName,
            Topic = "orders",
            RoutingKey = "orders",
            IsRaw = false
        };

        var envelope = CreateEnvelope(outboxSerializer, outboxPayload);
        var publisher = provider.GetRequiredService<SquirrelBoxPigeonOutboxPublisher>();

        var result = await publisher.PublishAsync(envelope);
        var manager = provider.GetRequiredService<FakeProducingManager>();

        Assert.True(result.Succeeded);
        var published = Assert.Single(manager.Wrapped);
        Assert.Equal("orders", published.Route.Topic);
        Assert.IsType<WrappedPayload<OrderMessage>>(published.Payload);
    }

    [Fact]
    public async Task PigeonOutboxPublisher_rehydrates_raw_payload_and_pushes_raw()
    {
        using var provider = CreateProvider();
        var outboxSerializer = provider.GetRequiredService<IOutboxEnvelopeSerializer>();
        var pigeonSerializer = provider.GetRequiredService<ISerializer>();
        var outboxPayload = new SquirrelBoxPigeonOutboxPayload
        {
            Payload = pigeonSerializer.Serialize(new OrderMessage("order-raw")),
            PayloadType = typeof(OrderMessage).AssemblyQualifiedName,
            Topic = "orders",
            RoutingKey = "orders",
            IsRaw = true
        };

        var envelope = CreateEnvelope(outboxSerializer, outboxPayload);
        var publisher = provider.GetRequiredService<SquirrelBoxPigeonOutboxPublisher>();

        await publisher.PublishAsync(envelope);
        var manager = provider.GetRequiredService<FakeProducingManager>();

        var published = Assert.Single(manager.Raw);
        Assert.Equal("orders", published.Route.Topic);
        Assert.IsType<OrderMessage>(published.Payload);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISerializer, JsonPigeonSerializer>();
        services.AddSingleton(Options.Create(new GlobalSettings { Domain = "sales" }));
        services.AddSingleton(Options.Create(new SquirrelBoxPigeonOptions { EnableOutbox = true }));
        services.AddSingleton<FakeProducingManager>();
        services.AddSingleton<IProducingManager>(provider => provider.GetRequiredService<FakeProducingManager>());
        services.AddSingleton<SquirrelBoxPigeonOutboxPublisher>();
        services.AddSingleton<IOutboxTransportPublisher>(provider => provider.GetRequiredService<SquirrelBoxPigeonOutboxPublisher>());
        services.AddSquirrelBox().UseInMemory();
        return services.BuildServiceProvider();
    }

    private static PublishContext CreatePublishContext(bool isRaw)
    {
        var context = new PublishContext
        {
            IsRaw = isRaw,
            Message = new OrderMessage("order-1"),
            MessageType = typeof(OrderMessage),
            Version = SemanticVersion.Parse("1.2.0")
        };

        typeof(PublishContext)
            .GetProperty(nameof(PublishContext.Route), BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(context, PublishingRoute.ForTopic("orders"));

        return context;
    }

    private static OutboxEnvelope CreateEnvelope(
        IOutboxEnvelopeSerializer serializer,
        SquirrelBoxPigeonOutboxPayload payload)
        => new()
        {
            Id = Ulid.NewUlid(),
            Transport = "pigeon",
            Operation = "pigeon.publish",
            Destination = "orders",
            PayloadType = typeof(SquirrelBoxPigeonOutboxPayload).AssemblyQualifiedName,
            Payload = serializer.Serialize(payload, typeof(SquirrelBoxPigeonOutboxPayload)),
            ContentType = serializer.ContentType,
            Status = OutboxStatus.Pending,
            CreatedOnUtc = DateTimeOffset.UtcNow,
            UpdatedOnUtc = DateTimeOffset.UtcNow
        };

    private sealed record OrderMessage(string Id);

    private sealed class JsonPigeonSerializer : ISerializer
    {
        private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

        public string Serialize(object payload)
            => JsonSerializer.Serialize(payload, payload.GetType(), Options);

        public object Deserialize(string rawJson, Type targetType)
            => JsonSerializer.Deserialize(rawJson, targetType, Options);
    }

    private sealed class FakeProducingManager : IProducingManager
    {
        public IList<(object Payload, PublishingRoute Route)> Wrapped { get; } = [];

        public IList<(object Payload, PublishingRoute Route)> Raw { get; } = [];

        public ValueTask PushAsync<T>(
            WrappedPayload<T> payload,
            string topic,
            CancellationToken cancellationToken = default)
            where T : class
            => PushAsync(payload, PublishingRoute.ForTopic(topic), cancellationToken);

        public ValueTask PushAsync<T>(
            WrappedPayload<T> payload,
            PublishingRoute route,
            CancellationToken cancellationToken = default)
            where T : class
        {
            Wrapped.Add((payload, route));
            return ValueTask.CompletedTask;
        }

        public ValueTask PushRawAsync<T>(
            T message,
            string topic,
            CancellationToken cancellationToken = default)
            where T : class
            => PushRawAsync(message, PublishingRoute.ForTopic(topic), cancellationToken);

        public ValueTask PushRawAsync<T>(
            T message,
            PublishingRoute route,
            CancellationToken cancellationToken = default)
            where T : class
        {
            Raw.Add((message, route));
            return ValueTask.CompletedTask;
        }

        public ValueTask PushOutboxAsync(OutboxMessage message, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
