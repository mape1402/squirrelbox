using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pigeon.Messaging.Contracts;
using Pigeon.Messaging.Producing;
using SquirrelBox.InMemory;
using SquirrelBox.Messaging.Pigeon;

namespace SquirrelBox.Messaging.Tests;

public sealed class SquirrelBoxPigeonOutboxTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task PublishDecisionInterceptor_persists_pigeon_publish_envelope_in_squirrelbox_outbox()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonOutboxInterceptor>(scope.ServiceProvider);
        var context = CreatePublishContext(isRaw: false);
        context.Operation = "create-order";
        context.CorrelationId = "corr-1";
        context.TraceId = "trace-1";
        context.AddHeader("x-tenant", "north");
        context.AddMetadata("tenant", "north");

        var result = await interceptor.InterceptAsync(context);
        var envelopes = await scope.ServiceProvider.GetRequiredService<IOutboxStore>()
            .QueryAsync(new OutboxQuery { Transport = "pigeon" });

        var envelope = Assert.Single(envelopes);
        var serializer = scope.ServiceProvider.GetRequiredService<IOutboxEnvelopeSerializer>();
        var publishEnvelope = (PigeonPublishEnvelope)serializer.Deserialize(
            envelope.Payload,
            typeof(PigeonPublishEnvelope));

        Assert.Equal(PigeonPublishDecision.Skip, result.Decision);
        Assert.Equal(envelope.Id.ToString(), result.Metadata["squirrelbox-outbox-id"]);
        Assert.Equal("pigeon.publish", envelope.Operation);
        Assert.Equal("orders", envelope.Destination);
        Assert.Equal(typeof(PigeonPublishEnvelope).AssemblyQualifiedName, envelope.PayloadType);
        Assert.Equal("1.2.0", envelope.Metadata["squirrelbox-pigeon-version"]);
        Assert.Equal("create-order", envelope.Metadata["pigeon-operation"]);
        Assert.Equal("north", envelope.Headers["x-tenant"]);
        Assert.Equal("corr-1", envelope.CorrelationId);
        Assert.Equal("trace-1", envelope.TraceId);
        Assert.Equal("orders", publishEnvelope.Topic);
        Assert.Equal("1.2.0", publishEnvelope.Version);
        Assert.Equal("north", publishEnvelope.Metadata["tenant"]);
        Assert.Equal("north", publishEnvelope.Headers["x-tenant"]);
    }

    [Fact]
    public async Task PigeonOutboxPublisher_replays_pigeon_publish_envelope_through_pigeon_invoker()
    {
        using var provider = CreateProvider();
        var outboxSerializer = provider.GetRequiredService<IOutboxEnvelopeSerializer>();
        var publishEnvelope = CreatePublishEnvelope(isRaw: false);
        var envelope = CreateEnvelope(outboxSerializer, publishEnvelope, typeof(PigeonPublishEnvelope));
        var publisher = provider.GetRequiredService<SquirrelBoxPigeonOutboxPublisher>();

        var result = await publisher.PublishAsync(envelope);
        var invoker = provider.GetRequiredService<FakePigeonPublisherInvoker>();

        Assert.True(result.Succeeded);
        var published = Assert.Single(invoker.Published);
        Assert.False(published.IsRaw);
        Assert.Equal("orders", published.Topic);
        Assert.Equal("orders", published.Destination);
        Assert.Equal(typeof(OrderMessage).AssemblyQualifiedName, published.PayloadType);
        Assert.Equal(JsonSerializer.Serialize(new OrderMessage("order-1"), JsonOptions), Encoding.UTF8.GetString(published.Payload));
    }

    [Fact]
    public async Task PigeonOutboxPublisher_converts_legacy_payload_to_pigeon_publish_envelope()
    {
        using var provider = CreateProvider();
        var outboxSerializer = provider.GetRequiredService<IOutboxEnvelopeSerializer>();
        var legacyPayload = new SquirrelBoxPigeonOutboxPayload
        {
            Payload = JsonSerializer.Serialize(new OrderMessage("order-legacy"), JsonOptions),
            PayloadType = typeof(OrderMessage).AssemblyQualifiedName,
            Topic = "orders",
            RoutingKey = "orders",
            IsRaw = true
        };

        var envelope = CreateEnvelope(outboxSerializer, legacyPayload, typeof(SquirrelBoxPigeonOutboxPayload));
        envelope.Metadata["squirrelbox-pigeon-version"] = "1.2.0";
        envelope.CorrelationId = "corr-legacy";
        var publisher = provider.GetRequiredService<SquirrelBoxPigeonOutboxPublisher>();

        var result = await publisher.PublishAsync(envelope);
        var invoker = provider.GetRequiredService<FakePigeonPublisherInvoker>();

        Assert.True(result.Succeeded);
        var published = Assert.Single(invoker.Published);
        Assert.True(published.IsRaw);
        Assert.Equal("orders", published.Topic);
        Assert.Equal("1.2.0", published.Version);
        Assert.Equal("corr-legacy", published.CorrelationId);
        Assert.Equal(legacyPayload.Payload, Encoding.UTF8.GetString(published.Payload));
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Options.Create(new SquirrelBoxPigeonOptions { EnableOutbox = true }));
        services.AddSingleton<FakePigeonPublishEnvelopeFactory>();
        services.AddSingleton<IPigeonPublishEnvelopeFactory>(
            provider => provider.GetRequiredService<FakePigeonPublishEnvelopeFactory>());
        services.AddSingleton<FakePigeonPublisherInvoker>();
        services.AddSingleton<IPigeonPublisherInvoker>(
            provider => provider.GetRequiredService<FakePigeonPublisherInvoker>());
        services.AddSingleton<SquirrelBoxPigeonOutboxPublisher>();
        services.AddSingleton<IOutboxTransportPublisher>(
            provider => provider.GetRequiredService<SquirrelBoxPigeonOutboxPublisher>());
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

    private static PigeonPublishEnvelope CreatePublishEnvelope(bool isRaw)
        => new()
        {
            Transport = "pigeon",
            Topic = "orders",
            Version = "1.2.0",
            Operation = "create-order",
            Destination = "orders",
            RoutingKey = "orders",
            ContentType = "application/json",
            Payload = JsonSerializer.SerializeToUtf8Bytes(new OrderMessage("order-1"), JsonOptions),
            PayloadType = typeof(OrderMessage).AssemblyQualifiedName,
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["x-tenant"] = "north"
            },
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tenant"] = "north"
            },
            CorrelationId = "corr-1",
            TraceId = "trace-1",
            IsRaw = isRaw
        };

    private static OutboxEnvelope CreateEnvelope(
        IOutboxEnvelopeSerializer serializer,
        object payload,
        Type payloadType)
        => new()
        {
            Id = Ulid.NewUlid(),
            Transport = "pigeon",
            Operation = "pigeon.publish",
            Destination = "orders",
            PayloadType = payloadType.AssemblyQualifiedName,
            Payload = serializer.Serialize(payload, payloadType),
            ContentType = serializer.ContentType,
            Status = OutboxStatus.Pending,
            CreatedOnUtc = DateTimeOffset.UtcNow,
            UpdatedOnUtc = DateTimeOffset.UtcNow
        };

    private sealed record OrderMessage(string Id);

    private sealed class FakePigeonPublishEnvelopeFactory : IPigeonPublishEnvelopeFactory
    {
        public ValueTask<PigeonPublishEnvelope> CreateAsync(
            PublishContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var route = context.Route;
            var envelope = new PigeonPublishEnvelope
            {
                Transport = context.Transport,
                Topic = route.Topic,
                Version = context.Version.ToString(),
                Operation = context.Operation ?? context.MessageType?.Name,
                Destination = !string.IsNullOrWhiteSpace(route.Exchange)
                    ? $"{route.Exchange}:{route.RoutingKey}"
                    : route.Topic,
                Exchange = route.Exchange,
                RoutingKey = route.RoutingKey,
                ContentType = context.ContentType,
                Payload = JsonSerializer.SerializeToUtf8Bytes(context.Message, context.MessageType, JsonOptions),
                PayloadType = context.MessageType.AssemblyQualifiedName,
                Headers = new Dictionary<string, string>(context.Headers, StringComparer.OrdinalIgnoreCase),
                Metadata = context.Metadata.ToDictionary(
                    item => item.Key,
                    item => item.Value?.ToString(),
                    StringComparer.OrdinalIgnoreCase),
                CorrelationId = context.CorrelationId,
                TraceId = context.TraceId,
                IsRaw = context.IsRaw
            };

            return ValueTask.FromResult(envelope);
        }
    }

    private sealed class FakePigeonPublisherInvoker : IPigeonPublisherInvoker
    {
        public IList<PigeonPublishEnvelope> Published { get; } = [];

        public ValueTask PublishAsync(
            PigeonPublishEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Published.Add(envelope);
            return ValueTask.CompletedTask;
        }
    }
}
