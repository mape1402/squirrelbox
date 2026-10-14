using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pigeon.Messaging;
using Pigeon.Messaging.Contracts;
using Pigeon.Messaging.Producing;
using SquirrelBox.InMemory;
using SquirrelBox.Messaging.Pigeon;

namespace SquirrelBox.Messaging.Tests;

public sealed class SquirrelBoxPigeonOutboxTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

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
        Assert.Equal(typeof(PigeonPublishEnvelope).FullName, envelope.PayloadType);
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
    public async Task PublishDecisionInterceptor_enriches_pigeon_envelope_with_current_squirrelbox_identity()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        var opened = await inbox.OpenOrContinueAsync(new InboxOpenRequest
        {
            Source = "pigeon",
            Operation = "orders:1.2.0/billing/OrderMessage",
            IdempotencyKey = "consume-key",
            CorrelationId = "consume-correlation",
            TraceId = "consume-trace"
        });
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonOutboxInterceptor>(scope.ServiceProvider);
        var context = CreatePublishContext(isRaw: false);

        var result = await interceptor.InterceptAsync(context);
        var envelopes = await scope.ServiceProvider.GetRequiredService<IOutboxStore>()
            .QueryAsync(new OutboxQuery { Transport = "pigeon" });

        var envelope = Assert.Single(envelopes);
        var serializer = scope.ServiceProvider.GetRequiredService<IOutboxEnvelopeSerializer>();
        var publishEnvelope = (PigeonPublishEnvelope)serializer.Deserialize(
            envelope.Payload,
            typeof(PigeonPublishEnvelope));

        Assert.Equal(PigeonPublishDecision.Skip, result.Decision);
        var outboxMetadata = ReadSquirrelBoxMetadata(envelope.Metadata);
        var publishMetadata = ReadSquirrelBoxMetadata(publishEnvelope.Metadata);
        Assert.Equal(opened.EffectiveIdempotencyKey, outboxMetadata.IdempotencyKey);
        Assert.Equal(opened.EffectiveCorrelationId, outboxMetadata.CorrelationId);
        Assert.Equal(opened.EffectiveAttemptId, outboxMetadata.AttemptId);
        Assert.Equal(opened.EffectiveTraceId, outboxMetadata.TraceId);
        Assert.Equal(opened.EffectiveIdempotencyKey, publishMetadata.IdempotencyKey);
        Assert.Equal(opened.EffectiveCorrelationId, publishMetadata.CorrelationId);
        Assert.Equal(opened.EffectiveAttemptId, publishMetadata.AttemptId);
        Assert.Equal(opened.EffectiveTraceId, publishMetadata.TraceId);
        var wrappedMetadata = ReadWrappedPayloadSquirrelBoxMetadata(publishEnvelope.Payload);
        Assert.Equal(opened.EffectiveIdempotencyKey, wrappedMetadata.IdempotencyKey);
        Assert.Equal(opened.EffectiveCorrelationId, wrappedMetadata.CorrelationId);
        Assert.Equal(opened.EffectiveAttemptId, wrappedMetadata.AttemptId);
        Assert.Equal(opened.EffectiveTraceId, wrappedMetadata.TraceId);
        AssertNoFlatIdentityMetadata(envelope.Metadata);
        AssertNoFlatIdentityMetadata(publishEnvelope.Metadata);
        Assert.Equal(opened.EffectiveCorrelationId, envelope.CorrelationId);
        Assert.Equal(opened.EffectiveTraceId, envelope.TraceId);
    }

    [Fact]
    public async Task PublishDecisionInterceptor_continues_when_outbox_is_disabled_or_predicate_rejects()
    {
        using var disabledProvider = CreateProvider(configurePigeon: options => options.EnableOutbox = false);
        using var disabledScope = disabledProvider.CreateScope();
        var disabled = await ActivatorUtilities
            .CreateInstance<SquirrelBoxPigeonOutboxInterceptor>(disabledScope.ServiceProvider)
            .InterceptAsync(CreatePublishContext(isRaw: false));

        using var predicateProvider = CreateProvider(configurePigeon: options => options.OutboxPredicate = _ => false);
        using var predicateScope = predicateProvider.CreateScope();
        var rejected = await ActivatorUtilities
            .CreateInstance<SquirrelBoxPigeonOutboxInterceptor>(predicateScope.ServiceProvider)
            .InterceptAsync(CreatePublishContext(isRaw: false));

        Assert.Equal(PigeonPublishDecision.Continue, disabled.Decision);
        Assert.Equal(PigeonPublishDecision.Continue, rejected.Decision);
    }

    [Fact]
    public async Task PublishDecisionInterceptor_handles_null_headers_metadata_and_exchange_destination()
    {
        var publishEnvelope = new PigeonPublishEnvelope
        {
            Transport = "pigeon",
            Version = "1.2.0",
            Operation = "create-order",
            Exchange = "events",
            RoutingKey = "orders.created",
            ContentType = "application/json",
            Payload = JsonSerializer.SerializeToUtf8Bytes(new OrderMessage("order-1"), JsonOptions),
            PayloadType = typeof(OrderMessage).AssemblyQualifiedName,
            CorrelationId = "corr-1",
            TraceId = "trace-1",
            IsRaw = true
        };
        using var provider = CreateProvider(envelopeFactory: new FixedPigeonPublishEnvelopeFactory(publishEnvelope));
        using var scope = provider.CreateScope();

        var result = await ActivatorUtilities
            .CreateInstance<SquirrelBoxPigeonOutboxInterceptor>(scope.ServiceProvider)
            .InterceptAsync(CreatePublishContext(isRaw: true));
        var envelope = Assert.Single(await scope.ServiceProvider.GetRequiredService<IOutboxStore>()
            .QueryAsync(new OutboxQuery { Transport = "pigeon" }));

        Assert.Equal(PigeonPublishDecision.Skip, result.Decision);
        Assert.Equal("events:orders.created", envelope.Destination);
        Assert.Empty(envelope.Headers);
        Assert.Equal("True", envelope.Metadata["pigeon-is-raw"]);
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
    public async Task PigeonOutboxPublisher_restores_structured_metadata_from_outbox_envelope_when_publish_envelope_is_missing_it()
    {
        using var provider = CreateProvider();
        var outboxSerializer = provider.GetRequiredService<IOutboxEnvelopeSerializer>();
        var publishEnvelope = CreatePublishEnvelope(isRaw: false);
        var envelope = CreateEnvelope(outboxSerializer, publishEnvelope, typeof(PigeonPublishEnvelope));
        new SquirrelBoxMessageMetadata
        {
            Metadata = new SquirrelBoxMetadata
            {
                IdempotencyKey = "outbox-key",
                CorrelationId = "outbox-correlation",
                TraceId = "outbox-trace",
                AttemptId = "outbox-attempt"
            }
        }.WriteTo(envelope.Metadata);
        var publisher = provider.GetRequiredService<SquirrelBoxPigeonOutboxPublisher>();

        var result = await publisher.PublishAsync(envelope);
        var invoker = provider.GetRequiredService<FakePigeonPublisherInvoker>();

        Assert.True(result.Succeeded);
        var published = Assert.Single(invoker.Published);
        var metadata = ReadSquirrelBoxMetadata(published.Metadata);
        Assert.Equal("outbox-key", metadata.IdempotencyKey);
        Assert.Equal("outbox-correlation", metadata.CorrelationId);
        Assert.Equal("outbox-trace", metadata.TraceId);
        Assert.Equal("outbox-attempt", metadata.AttemptId);
        AssertNoFlatIdentityMetadata(published.Metadata);
    }

    [Fact]
    public async Task PigeonOutboxPublisher_preserves_invalid_structured_metadata_as_envelope_metadata_only()
    {
        using var provider = CreateProvider();
        var outboxSerializer = provider.GetRequiredService<IOutboxEnvelopeSerializer>();
        var publishEnvelope = CreateWrappedPublishEnvelope(includeSquirrelBoxMetadata: false);
        var envelope = CreateEnvelope(outboxSerializer, publishEnvelope, typeof(PigeonPublishEnvelope));
        envelope.Metadata[SquirrelBoxMetadataNames.MetadataSection] = "{not-json";
        var publisher = provider.GetRequiredService<SquirrelBoxPigeonOutboxPublisher>();

        var result = await publisher.PublishAsync(envelope);
        var invoker = provider.GetRequiredService<FakePigeonPublisherInvoker>();

        Assert.True(result.Succeeded);
        var published = Assert.Single(invoker.Published);
        Assert.Equal("{not-json", published.Metadata[SquirrelBoxMetadataNames.MetadataSection]);
        using var json = JsonDocument.Parse(published.Payload);
        var metadata = ReadMetadataObject(json.RootElement);
        Assert.False(metadata.TryGetProperty(SquirrelBoxMetadataNames.MetadataSection, out _));
    }

    [Fact]
    public async Task PigeonOutboxPublisher_adds_durable_metadata_to_raw_publish_without_rewriting_payload()
    {
        using var provider = CreateProvider();
        var outboxSerializer = provider.GetRequiredService<IOutboxEnvelopeSerializer>();
        var publishEnvelope = CreatePublishEnvelope(isRaw: true);
        var originalPayload = publishEnvelope.Payload.ToArray();
        var envelope = CreateEnvelope(outboxSerializer, publishEnvelope, typeof(PigeonPublishEnvelope));
        new SquirrelBoxMessageMetadata
        {
            Metadata = new SquirrelBoxMetadata
            {
                IdempotencyKey = "raw-key"
            }
        }.WriteTo(envelope.Metadata);
        var publisher = provider.GetRequiredService<SquirrelBoxPigeonOutboxPublisher>();

        await publisher.PublishAsync(envelope);

        var published = Assert.Single(provider.GetRequiredService<FakePigeonPublisherInvoker>().Published);
        Assert.Equal(originalPayload, published.Payload);
        Assert.Equal("raw-key", ReadSquirrelBoxMetadata(published.Metadata).IdempotencyKey);
    }

    [Fact]
    public async Task PigeonOutboxPublisher_injects_structured_metadata_into_wrapped_payload_from_outbox_envelope()
    {
        using var provider = CreateProvider();
        var outboxSerializer = provider.GetRequiredService<IOutboxEnvelopeSerializer>();
        var publishEnvelope = CreateWrappedPublishEnvelope(includeSquirrelBoxMetadata: false);
        var envelope = CreateEnvelope(outboxSerializer, publishEnvelope, typeof(PigeonPublishEnvelope));
        new SquirrelBoxMessageMetadata
        {
            Metadata = new SquirrelBoxMetadata
            {
                IdempotencyKey = "outbox-key",
                CorrelationId = "outbox-correlation",
                TraceId = "outbox-trace",
                AttemptId = "outbox-attempt"
            }
        }.WriteTo(envelope.Metadata);
        var publisher = provider.GetRequiredService<SquirrelBoxPigeonOutboxPublisher>();

        var result = await publisher.PublishAsync(envelope);
        var invoker = provider.GetRequiredService<FakePigeonPublisherInvoker>();

        Assert.True(result.Succeeded);
        var published = Assert.Single(invoker.Published);
        var metadata = ReadWrappedPayloadSquirrelBoxMetadata(published.Payload);
        Assert.Equal("outbox-key", metadata.IdempotencyKey);
        Assert.Equal("outbox-correlation", metadata.CorrelationId);
        Assert.Equal("outbox-trace", metadata.TraceId);
        Assert.Equal("outbox-attempt", metadata.AttemptId);
        AssertWrappedPayloadMetadataIsObject(published.Payload);
    }

    [Fact]
    public async Task PigeonOutboxPublisher_does_not_rewrite_wrapped_payload_when_metadata_already_exists()
    {
        using var provider = CreateProvider();
        var outboxSerializer = provider.GetRequiredService<IOutboxEnvelopeSerializer>();
        var publishEnvelope = CreateWrappedPublishEnvelope(includeSquirrelBoxMetadata: true);
        var originalPayload = publishEnvelope.Payload.ToArray();
        var envelope = CreateEnvelope(outboxSerializer, publishEnvelope, typeof(PigeonPublishEnvelope));
        new SquirrelBoxMessageMetadata
        {
            Metadata = new SquirrelBoxMetadata
            {
                IdempotencyKey = "outbox-key"
            }
        }.WriteTo(envelope.Metadata);
        var publisher = provider.GetRequiredService<SquirrelBoxPigeonOutboxPublisher>();

        await publisher.PublishAsync(envelope);

        var published = Assert.Single(provider.GetRequiredService<FakePigeonPublisherInvoker>().Published);
        Assert.Equal(originalPayload, published.Payload);
        Assert.Equal("payload-key", ReadWrappedPayloadSquirrelBoxMetadata(published.Payload).IdempotencyKey);
    }

    [Fact]
    public async Task PigeonOutboxPublisher_rejects_unsupported_outbox_payload_type()
    {
        using var provider = CreateProvider();
        var outboxSerializer = provider.GetRequiredService<IOutboxEnvelopeSerializer>();
        var envelope = CreateEnvelope(outboxSerializer, "unsupported", typeof(string));
        var publisher = provider.GetRequiredService<SquirrelBoxPigeonOutboxPublisher>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => publisher.PublishAsync(envelope).AsTask());
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

    [Fact]
    public async Task PigeonOutboxPublisher_converts_null_legacy_payload_to_empty_body_without_version_metadata()
    {
        using var provider = CreateProvider();
        var outboxSerializer = provider.GetRequiredService<IOutboxEnvelopeSerializer>();
        var legacyPayload = new SquirrelBoxPigeonOutboxPayload
        {
            Payload = null,
            PayloadType = typeof(OrderMessage).AssemblyQualifiedName,
            Topic = "orders",
            IsRaw = true
        };
        var envelope = CreateEnvelope(outboxSerializer, legacyPayload, typeof(SquirrelBoxPigeonOutboxPayload));

        var result = await provider.GetRequiredService<SquirrelBoxPigeonOutboxPublisher>()
            .PublishAsync(envelope);

        Assert.True(result.Succeeded);
        var published = Assert.Single(provider.GetRequiredService<FakePigeonPublisherInvoker>().Published);
        Assert.Empty(published.Payload);
        Assert.Null(published.Version);
    }

    [Fact]
    public void PigeonMetadataSynchronizer_ignores_blank_metadata_json()
    {
        var envelope = CreatePublishEnvelope(isRaw: false);

        var enriched = EnrichWithMetadataJson(envelope, " ");

        Assert.Same(envelope, enriched);
    }

    [Fact]
    public void PigeonMetadataSynchronizer_ignores_missing_metadata_model()
    {
        var envelope = CreatePublishEnvelope(isRaw: false);

        var enriched = EnrichWithMetadata(envelope, null);

        Assert.Same(envelope, enriched);
    }

    [Fact]
    public void PigeonMetadataSynchronizer_requires_envelope_and_serializer()
    {
        var envelope = CreatePublishEnvelope(isRaw: false);

        Assert.Throws<TargetInvocationException>(() => EnrichWithMetadata(null, new SquirrelBoxMessageMetadata()));
        Assert.Throws<TargetInvocationException>(() => EnrichWithMetadata(envelope, new SquirrelBoxMessageMetadata(), serializer: null, useDefaultSerializer: false));
        Assert.Throws<TargetInvocationException>(() => EnrichWithMetadataJson(null, "{}"));
        Assert.Throws<TargetInvocationException>(() => EnrichWithMetadataJson(envelope, "{}", serializer: null, useDefaultSerializer: false));
    }

    [Fact]
    public void PigeonMetadataSynchronizer_preserves_invalid_json_on_envelope_metadata()
    {
        var envelope = CreatePublishEnvelope(isRaw: false, metadataNull: true);

        var enriched = EnrichWithMetadataJson(envelope, "{not-json");

        Assert.Equal("{not-json", enriched.Metadata[SquirrelBoxMetadataNames.MetadataSection]);
        Assert.Null(envelope.Metadata);
    }

    [Fact]
    public void PigeonMetadataSynchronizer_keeps_empty_structured_metadata_on_envelope_only()
    {
        var envelope = CreatePublishEnvelope(isRaw: false);

        var enriched = EnrichWithMetadataJson(envelope, "{}");

        Assert.Equal("{}", enriched.Metadata[SquirrelBoxMetadataNames.MetadataSection]);
        Assert.Equal(envelope.Payload, enriched.Payload);
    }

    [Fact]
    public void PigeonMetadataSynchronizer_keeps_json_null_metadata_on_envelope_only()
    {
        var envelope = CreatePublishEnvelope(isRaw: false);

        var enriched = EnrichWithMetadataJson(envelope, "null");

        Assert.Equal("null", enriched.Metadata[SquirrelBoxMetadataNames.MetadataSection]);
        Assert.Equal(envelope.Payload, enriched.Payload);
    }

    [Fact]
    public void PigeonMetadataSynchronizer_keeps_null_payload_unchanged()
    {
        var envelope = new PigeonPublishEnvelope
        {
            Transport = "pigeon",
            Topic = "orders",
            Payload = null,
            PayloadType = typeof(OrderMessage).AssemblyQualifiedName,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };
        var metadataJson = JsonSerializer.Serialize(
            new SquirrelBoxMetadata { IdempotencyKey = "key" },
            JsonOptions);

        var enriched = EnrichWithMetadataJson(envelope, metadataJson);

        Assert.Null(enriched.Payload);
    }

    [Theory]
    [InlineData(true, "")]
    [InlineData(false, "")]
    [InlineData(false, "System.String")]
    public void PigeonMetadataSynchronizer_keeps_payload_when_raw_empty_or_not_wrapped(bool isRaw, string payloadType)
    {
        var envelope = CreatePublishEnvelope(isRaw, payload: [], payloadType: payloadType);
        var metadataJson = JsonSerializer.Serialize(new SquirrelBoxMetadata
        {
            IdempotencyKey = "key"
        }, JsonOptions);

        var enriched = EnrichWithMetadataJson(envelope, metadataJson);

        Assert.Equal(envelope.Payload, enriched.Payload);
        Assert.Equal("key", ReadSquirrelBoxMetadata(enriched.Metadata).IdempotencyKey);
    }

    [Fact]
    public void PigeonMetadataSynchronizer_creates_wrapped_metadata_when_payload_metadata_is_null()
    {
        var envelope = CreateWrappedPublishEnvelopeWithMetadata(metadata: null);
        var metadataJson = JsonSerializer.Serialize(new SquirrelBoxMetadata
        {
            IdempotencyKey = "key-from-null"
        }, JsonOptions);

        var enriched = EnrichWithMetadataJson(envelope, metadataJson);

        Assert.Equal("key-from-null", ReadWrappedPayloadSquirrelBoxMetadata(enriched.Payload).IdempotencyKey);
    }

    [Fact]
    public void PigeonMetadataSynchronizer_replaces_string_metadata_section_with_structured_object()
    {
        var envelope = CreateWrappedPublishEnvelopeWithMetadata(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            [SquirrelBoxMetadataNames.MetadataSection] = "legacy-string"
        });
        var metadataJson = JsonSerializer.Serialize(new SquirrelBoxMetadata
        {
            IdempotencyKey = "key-from-string"
        }, JsonOptions);

        var enriched = EnrichWithMetadataJson(envelope, metadataJson);

        Assert.Equal("key-from-string", ReadWrappedPayloadSquirrelBoxMetadata(enriched.Payload).IdempotencyKey);
    }

    [Fact]
    public void PigeonMetadataSynchronizer_preserves_direct_structured_payload_metadata()
    {
        var envelope = CreateWrappedPublishEnvelopeWithMetadata(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase));
        var payload = CreateWrappedPayloadWithDirectMetadata(new SquirrelBoxMetadata
        {
            IdempotencyKey = "payload-key"
        });
        var serializer = new PayloadReturningSerializer(payload);
        var originalPayload = envelope.Payload.ToArray();
        var metadataJson = JsonSerializer.Serialize(new SquirrelBoxMetadata
        {
            IdempotencyKey = "ignored"
        }, JsonOptions);

        var enriched = EnrichWithMetadataJson(envelope, metadataJson, serializer);

        Assert.Equal(originalPayload, enriched.Payload);
    }

    [Fact]
    public void PigeonMetadataSynchronizer_replaces_direct_string_payload_metadata()
    {
        var envelope = CreateWrappedPublishEnvelopeWithMetadata(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase));
        var payload = CreateWrappedPayloadWithDirectMetadata("legacy-string");
        var metadataJson = JsonSerializer.Serialize(new SquirrelBoxMetadata
        {
            IdempotencyKey = "rewritten-key"
        }, JsonOptions);

        var enriched = EnrichWithMetadataJson(envelope, metadataJson, new PayloadReturningSerializer(payload));

        Assert.Equal("rewritten-key", ReadWrappedPayloadSquirrelBoxMetadata(enriched.Payload).IdempotencyKey);
    }

    [Fact]
    public void PigeonMetadataSynchronizer_preserves_unknown_structured_metadata_section()
    {
        var envelope = CreateWrappedPublishEnvelopeWithMetadata(new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            [SquirrelBoxMetadataNames.MetadataSection] = true
        });
        var originalPayload = envelope.Payload.ToArray();
        var metadataJson = JsonSerializer.Serialize(new SquirrelBoxMetadata
        {
            IdempotencyKey = "ignored"
        }, JsonOptions);

        var enriched = EnrichWithMetadataJson(envelope, metadataJson);

        Assert.Equal(originalPayload, enriched.Payload);
    }

    [Fact]
    public void PigeonMetadataSynchronizer_preserves_payload_when_serializer_returns_null()
    {
        var envelope = CreateWrappedPublishEnvelope(includeSquirrelBoxMetadata: false);
        var originalPayload = envelope.Payload.ToArray();
        var metadataJson = JsonSerializer.Serialize(new SquirrelBoxMetadata
        {
            IdempotencyKey = "ignored"
        }, JsonOptions);

        var enriched = EnrichWithMetadataJson(envelope, metadataJson, new NullPigeonSerializer());

        Assert.Equal(originalPayload, enriched.Payload);
        Assert.Equal("ignored", ReadSquirrelBoxMetadata(enriched.Metadata).IdempotencyKey);
    }

    private static ServiceProvider CreateProvider(
        Action<SquirrelBoxPigeonOptions> configurePigeon = null,
        IPigeonPublishEnvelopeFactory envelopeFactory = null)
    {
        var services = new ServiceCollection();
        var pigeonOptions = new SquirrelBoxPigeonOptions { EnableOutbox = true };
        configurePigeon?.Invoke(pigeonOptions);
        services.AddSingleton(Options.Create(pigeonOptions));
        services.AddSingleton<FakePigeonPublishEnvelopeFactory>();
        services.AddSingleton<ISerializer, FakePigeonSerializer>();
        services.AddSingleton<IPigeonPublishEnvelopeFactory>(
            provider => envelopeFactory ?? provider.GetRequiredService<FakePigeonPublishEnvelopeFactory>());
        services.AddSingleton<FakePigeonPublisherInvoker>();
        services.AddSingleton<IPigeonPublisherInvoker>(
            provider => provider.GetRequiredService<FakePigeonPublisherInvoker>());
        services.AddSingleton<SquirrelBoxPigeonOutboxPublisher>();
        services.AddSingleton<IOutboxTransportPublisher>(
            provider => provider.GetRequiredService<SquirrelBoxPigeonOutboxPublisher>());
        services.AddSquirrelBox().UseInMemory();
        services.AddSquirrelBoxMessaging();
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

    private static PigeonPublishEnvelope CreatePublishEnvelope(
        bool isRaw,
        byte[] payload = null,
        string payloadType = null,
        IReadOnlyDictionary<string, string> metadata = null,
        bool metadataNull = false)
        => new()
        {
            Transport = "pigeon",
            Topic = "orders",
            Version = "1.2.0",
            Operation = "create-order",
            Destination = "orders",
            RoutingKey = "orders",
            ContentType = "application/json",
            Payload = payload ?? JsonSerializer.SerializeToUtf8Bytes(new OrderMessage("order-1"), JsonOptions),
            PayloadType = payloadType ?? typeof(OrderMessage).AssemblyQualifiedName,
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["x-tenant"] = "north"
            },
            Metadata = metadataNull
                ? null
                : new Dictionary<string, string>(
                    metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["tenant"] = "north"
                    },
                    StringComparer.OrdinalIgnoreCase),
            CorrelationId = "corr-1",
            TraceId = "trace-1",
            IsRaw = isRaw
        };

    private static PigeonPublishEnvelope CreateWrappedPublishEnvelope(bool includeSquirrelBoxMetadata)
    {
        var metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["tenant"] = "north"
        };

        if (includeSquirrelBoxMetadata)
        {
            metadata[SquirrelBoxMetadataNames.MetadataSection] = new SquirrelBoxMetadata
            {
                IdempotencyKey = "payload-key",
                CorrelationId = "payload-correlation",
                TraceId = "payload-trace",
                AttemptId = "payload-attempt"
            };
        }

        var wrappedPayload = new WrappedPayload<OrderMessage>
        {
            Domain = "tests",
            MessageVersion = SemanticVersion.Parse("1.2.0"),
            CreatedOnUtc = DateTimeOffset.UtcNow,
            Message = new OrderMessage("order-1"),
            Metadata = metadata
        };

        return new PigeonPublishEnvelope
        {
            Transport = "pigeon",
            Topic = "orders",
            Version = "1.2.0",
            Operation = "create-order",
            Destination = "orders",
            RoutingKey = "orders",
            ContentType = "application/json",
            Payload = JsonSerializer.SerializeToUtf8Bytes(wrappedPayload, JsonOptions),
            PayloadType = typeof(WrappedPayload<OrderMessage>).AssemblyQualifiedName,
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["x-tenant"] = "north"
            },
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["tenant"] = "north"
            },
            CorrelationId = "corr-1",
            TraceId = "trace-1"
        };
    }

    private static PigeonPublishEnvelope CreateWrappedPublishEnvelopeWithMetadata(Dictionary<string, object> metadata)
    {
        var wrappedPayload = new WrappedPayload<OrderMessage>
        {
            Domain = "tests",
            MessageVersion = SemanticVersion.Parse("1.2.0"),
            CreatedOnUtc = DateTimeOffset.UtcNow,
            Message = new OrderMessage("order-1"),
            Metadata = metadata
        };

        return new PigeonPublishEnvelope
        {
            Transport = "pigeon",
            Topic = "orders",
            Version = "1.2.0",
            Operation = "create-order",
            Destination = "orders",
            RoutingKey = "orders",
            ContentType = "application/json",
            Payload = JsonSerializer.SerializeToUtf8Bytes(wrappedPayload, JsonOptions),
            PayloadType = typeof(WrappedPayload<OrderMessage>).AssemblyQualifiedName,
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            CorrelationId = "corr-1",
            TraceId = "trace-1"
        };
    }

    private static WrappedPayload<OrderMessage> CreateWrappedPayloadWithDirectMetadata(object metadataValue)
        => new()
        {
            Domain = "tests",
            MessageVersion = SemanticVersion.Parse("1.2.0"),
            CreatedOnUtc = DateTimeOffset.UtcNow,
            Message = new OrderMessage("order-1"),
            Metadata = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            {
                [SquirrelBoxMetadataNames.MetadataSection] = metadataValue
            }
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
            PayloadType = new DefaultOutboxPayloadTypeNameResolver().GetName(payloadType),
            Payload = serializer.Serialize(payload, payloadType),
            ContentType = serializer.ContentType,
            Status = OutboxStatus.Pending,
            CreatedOnUtc = DateTimeOffset.UtcNow,
            UpdatedOnUtc = DateTimeOffset.UtcNow
        };

    private sealed record OrderMessage(string Id);

    private static SquirrelBoxMetadata ReadSquirrelBoxMetadata(IEnumerable<KeyValuePair<string, string>> metadata)
    {
        Assert.True(SquirrelBoxMessageMetadata.TryReadFrom(metadata, out var squirrelBoxMetadata));
        return squirrelBoxMetadata.Metadata;
    }

    private static SquirrelBoxMetadata ReadWrappedPayloadSquirrelBoxMetadata(byte[] payload)
    {
        using var json = JsonDocument.Parse(payload);
        var metadata = ReadMetadataObject(json.RootElement);
        Assert.True(metadata.TryGetProperty(SquirrelBoxMetadataNames.MetadataSection, out var section));

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [SquirrelBoxMetadataNames.MetadataSection] = section.GetRawText()
        };

        return ReadSquirrelBoxMetadata(values);
    }

    private static void AssertWrappedPayloadMetadataIsObject(byte[] payload)
    {
        using var json = JsonDocument.Parse(payload);
        var metadata = ReadMetadataObject(json.RootElement);
        Assert.Equal(JsonValueKind.Object, metadata.GetProperty(SquirrelBoxMetadataNames.MetadataSection).ValueKind);
    }

    private static JsonElement ReadMetadataObject(JsonElement root)
    {
        if (root.TryGetProperty("metadata", out var metadata))
            return metadata;

        Assert.True(root.TryGetProperty("Metadata", out metadata));
        return metadata;
    }

    private static void AssertNoFlatIdentityMetadata(IEnumerable<KeyValuePair<string, string>> metadata)
    {
        Assert.DoesNotContain(metadata, item => IsFlatIdentityKey(item.Key));
    }

    private static PigeonPublishEnvelope EnrichWithMetadataJson(
        PigeonPublishEnvelope envelope,
        string metadataJson,
        ISerializer serializer = null,
        bool useDefaultSerializer = true)
    {
        var synchronizer = typeof(SquirrelBoxPigeonOutboxPublisher)
            .Assembly
            .GetType("SquirrelBox.Messaging.Pigeon.SquirrelBoxPigeonMetadataSynchronizer", throwOnError: true);
        var method = synchronizer.GetMethod(
            "Enrich",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(PigeonPublishEnvelope), typeof(string), typeof(ISerializer), typeof(ILogger)],
            modifiers: null);

        return (PigeonPublishEnvelope)method.Invoke(null, [envelope, metadataJson, serializer ?? (useDefaultSerializer ? new FakePigeonSerializer() : null), null]);
    }

    private static PigeonPublishEnvelope EnrichWithMetadata(
        PigeonPublishEnvelope envelope,
        SquirrelBoxMessageMetadata metadata,
        ISerializer serializer = null,
        bool useDefaultSerializer = true)
    {
        var synchronizer = typeof(SquirrelBoxPigeonOutboxPublisher)
            .Assembly
            .GetType("SquirrelBox.Messaging.Pigeon.SquirrelBoxPigeonMetadataSynchronizer", throwOnError: true);
        var method = synchronizer.GetMethod(
            "Enrich",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: [typeof(PigeonPublishEnvelope), typeof(SquirrelBoxMessageMetadata), typeof(ISerializer), typeof(ILogger)],
            modifiers: null);

        return (PigeonPublishEnvelope)method.Invoke(null, [envelope, metadata, serializer ?? (useDefaultSerializer ? new FakePigeonSerializer() : null), null]);
    }

    private static bool IsFlatIdentityKey(string key)
    {
        return string.Equals(key, SquirrelBoxMetadataNames.IdempotencyKey, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, SquirrelBoxMetadataNames.CorrelationId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, SquirrelBoxMetadataNames.AttemptId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, SquirrelBoxMetadataNames.TraceId, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakePigeonPublishEnvelopeFactory : IPigeonPublishEnvelopeFactory
    {
        private readonly ISerializer _serializer;

        public FakePigeonPublishEnvelopeFactory(ISerializer serializer)
        {
            _serializer = serializer;
        }

        public ValueTask<PigeonPublishEnvelope> CreateAsync(
            PublishContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var route = context.Route;
            var payload = context.IsRaw
                ? context.Message
                : new WrappedPayload<OrderMessage>
                {
                    Domain = "tests",
                    MessageVersion = context.Version,
                    CreatedOnUtc = DateTimeOffset.UtcNow,
                    Message = (OrderMessage)context.Message,
                    Metadata = context.Metadata
                };
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
                Payload = Encoding.UTF8.GetBytes(_serializer.Serialize(payload)),
                PayloadType = payload.GetType().AssemblyQualifiedName,
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

    private sealed class FakePigeonSerializer : ISerializer
    {
        public string Serialize(object payload)
            => JsonSerializer.Serialize(payload, payload.GetType(), JsonOptions);

        public object Deserialize(string rawJson, Type targetType)
            => JsonSerializer.Deserialize(rawJson, targetType, JsonOptions);
    }

    private sealed class FixedPigeonPublishEnvelopeFactory(PigeonPublishEnvelope envelope) : IPigeonPublishEnvelopeFactory
    {
        public ValueTask<PigeonPublishEnvelope> CreateAsync(
            PublishContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(envelope);
    }

    private sealed class NullPigeonSerializer : ISerializer
    {
        public string Serialize(object payload)
            => JsonSerializer.Serialize(payload, payload.GetType(), JsonOptions);

        public object Deserialize(string rawJson, Type targetType)
            => null;
    }

    private sealed class PayloadReturningSerializer(object payload) : ISerializer
    {
        public string Serialize(object value)
            => JsonSerializer.Serialize(value, value.GetType(), JsonOptions);

        public object Deserialize(string rawJson, Type targetType)
            => payload;
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

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new SemanticVersionJsonConverter());
        return options;
    }
}
