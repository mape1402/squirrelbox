using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pigeon.Messaging;
using Pigeon.Messaging.Contracts;
using Pigeon.Messaging.Producing;
using SquirrelBox.Messaging.Pigeon;

namespace SquirrelBox.Messaging.Tests;

public sealed class PigeonInternalBranchTests
{
    private static readonly Type Synchronizer = typeof(SquirrelBoxPigeonOutboxPublisher).Assembly.GetType(
        "SquirrelBox.Messaging.Pigeon.SquirrelBoxPigeonMetadataSynchronizer",
        throwOnError: true)!;

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("missing.type", false)]
    public void Metadata_synchronizer_resolve_type_handles_missing_values(string typeName, bool expected)
    {
        var method = Synchronizer.GetMethod("ResolveType", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = method.Invoke(null, [typeName]);

        Assert.Equal(expected, result is not null);
    }

    [Fact]
    public void Metadata_synchronizer_resolve_type_handles_valid_type()
    {
        var method = Synchronizer.GetMethod("ResolveType", BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.Equal(typeof(string), method.Invoke(null, [typeof(string).AssemblyQualifiedName]));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(typeof(string), false)]
    [InlineData(typeof(List<string>), false)]
    [InlineData(typeof(WrappedPayload<string>), true)]
    public void Metadata_synchronizer_identifies_only_wrapped_payloads(Type type, bool expected)
    {
        var method = Synchronizer.GetMethod("IsWrappedPayload", BindingFlags.NonPublic | BindingFlags.Static)!;

        Assert.Equal(expected, method.Invoke(null, [type]));
    }

    [Fact]
    public void Metadata_synchronizer_mutable_metadata_handles_dictionary_and_non_dictionary_values()
    {
        var method = Synchronizer.GetMethod("ToMutableMetadata", BindingFlags.NonPublic | BindingFlags.Static)!;
        var source = new Dictionary<string, object> { ["tenant"] = "north" };

        var copied = Assert.IsType<Dictionary<string, object>>(method.Invoke(null, [source]));
        var empty = Assert.IsType<Dictionary<string, object>>(method.Invoke(null, [new object()]));

        Assert.Equal("north", copied["tenant"]);
        Assert.Empty(empty);
    }

    [Fact]
    public void Metadata_synchronizer_detects_every_structured_metadata_shape()
    {
        var method = Synchronizer.GetMethod("ContainsStructuredMetadata", BindingFlags.NonPublic | BindingFlags.Static)!;
        using var objectJson = JsonDocument.Parse("{}");
        using var stringJson = JsonDocument.Parse("\"value\"");

        Assert.False(InvokeContains(method, null));
        Assert.False(InvokeContains(method, new Dictionary<string, object>()));
        Assert.False(InvokeContains(method, Section(null)));
        Assert.False(InvokeContains(method, Section(new SquirrelBoxMetadata())));
        Assert.True(InvokeContains(method, Section(new SquirrelBoxMetadata { IdempotencyKey = "key" })));
        Assert.True(InvokeContains(method, Section(objectJson.RootElement)));
        Assert.False(InvokeContains(method, Section(stringJson.RootElement)));
        Assert.False(InvokeContains(method, Section("value")));
        Assert.True(InvokeContains(method, Section(42)));
    }

    [Fact]
    public void Metadata_synchronizer_clone_covers_null_and_populated_collections()
    {
        var method = Synchronizer.GetMethod("Clone", BindingFlags.NonPublic | BindingFlags.Static)!;
        var empty = new PigeonPublishEnvelope { Headers = null, Metadata = null };
        var populated = new PigeonPublishEnvelope
        {
            Headers = new Dictionary<string, string> { ["header"] = "value" },
            Metadata = new Dictionary<string, string> { ["metadata"] = "value" }
        };

        var emptyClone = Assert.IsType<PigeonPublishEnvelope>(method.Invoke(null, [empty, null, null]));
        var populatedClone = Assert.IsType<PigeonPublishEnvelope>(method.Invoke(
            null,
            [populated, Array.Empty<byte>(), populated.Metadata]));

        Assert.Null(emptyClone.Headers);
        Assert.Null(emptyClone.Metadata);
        Assert.Equal("value", populatedClone.Headers["header"]);
        Assert.Equal("value", populatedClone.Metadata["metadata"]);
    }

    [Theory]
    [InlineData("destination", "exchange", "route", "topic", "destination")]
    [InlineData(null, "exchange", "route", "topic", "exchange:route")]
    [InlineData(null, null, null, "topic", "topic")]
    public void Outbox_interceptor_resolves_destination_in_priority_order(
        string destination,
        string exchange,
        string routingKey,
        string topic,
        string expected)
    {
        var method = typeof(SquirrelBoxPigeonOutboxInterceptor).GetMethod(
            "ResolveDestination",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var envelope = new PigeonPublishEnvelope
        {
            Destination = destination,
            Exchange = exchange,
            RoutingKey = routingKey,
            Topic = topic
        };

        Assert.Equal(expected, method.Invoke(null, [envelope]));
    }

    [Fact]
    public void Outbox_interceptor_copy_handles_null_and_populated_sources()
    {
        var method = typeof(SquirrelBoxPigeonOutboxInterceptor).GetMethod(
            "Copy",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var target = new Dictionary<string, string>();

        method.Invoke(null, [null, target]);
        method.Invoke(null, [new Dictionary<string, string> { ["key"] = "value" }, target]);

        Assert.Equal("value", target["key"]);
    }

    [Fact]
    public void Outbox_interceptor_create_request_accepts_missing_identity_metadata()
    {
        var interceptor = new SquirrelBoxPigeonOutboxInterceptor(
            Substitute.For<IOutboxService>(),
            Substitute.For<IInboxService>(),
            Substitute.For<IPigeonPublishEnvelopeFactory>(),
            Substitute.For<ISerializer>(),
            Substitute.For<ISquirrelBoxMessageMetadataEnricher>(),
            Options.Create(new SquirrelBoxPigeonOptions()));
        var method = typeof(SquirrelBoxPigeonOutboxInterceptor).GetMethod(
            "CreateRequest",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var envelope = new PigeonPublishEnvelope
        {
            Transport = "pigeon",
            Topic = "orders",
            PayloadType = typeof(string).AssemblyQualifiedName,
            ContentType = "application/json"
        };

        var request = Assert.IsType<OutboxEnqueueRequest>(method.Invoke(interceptor, new object[] { envelope, null }));

        Assert.Null(request.CorrelationId);
        Assert.Null(request.TraceId);
    }

    [Fact]
    public void Outbox_interceptor_uses_last_context_identity_when_current_is_missing()
    {
        var inbox = Substitute.For<IInboxService>();
        var lastContext = new InboxContext(new InboxEntry(), "last", ownsCompletion: false);
        inbox.LastContext.Returns(lastContext);
        var enricher = Substitute.For<ISquirrelBoxMessageMetadataEnricher>();
        enricher.Create(Arg.Any<SquirrelBoxIdentity>()).Returns(SquirrelBoxMessageMetadata.Empty);
        var interceptor = new SquirrelBoxPigeonOutboxInterceptor(
            Substitute.For<IOutboxService>(),
            inbox,
            Substitute.For<IPigeonPublishEnvelopeFactory>(),
            Substitute.For<ISerializer>(),
            enricher,
            Options.Create(new SquirrelBoxPigeonOptions()));
        var method = typeof(SquirrelBoxPigeonOutboxInterceptor).GetMethod(
            "CreateCurrentMetadata",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var result = method.Invoke(interceptor, null);

        Assert.Same(SquirrelBoxMessageMetadata.Empty, result);
        enricher.Received(1).Create(Arg.Any<SquirrelBoxIdentity>());
    }

    [Fact]
    public void Outbox_publisher_durable_metadata_handles_missing_publish_envelope()
    {
        var publisher = new SquirrelBoxPigeonOutboxPublisher(
            Substitute.For<IPigeonPublisherInvoker>(),
            Substitute.For<IOutboxEnvelopeSerializer>(),
            Substitute.For<IOutboxPayloadTypeNameResolver>(),
            Substitute.For<ISerializer>(),
            Options.Create(new SquirrelBoxPigeonOptions()));
        var method = typeof(SquirrelBoxPigeonOutboxPublisher).GetMethod(
            "WithDurableMetadata",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        var outbox = new OutboxEnvelope();

        Assert.Null(method.Invoke(publisher, new object[] { null, outbox }));
        var publishEnvelope = new PigeonPublishEnvelope();
        Assert.Same(publishEnvelope, method.Invoke(publisher, new object[] { publishEnvelope, null }));
    }

    private static Dictionary<string, object> Section(object value)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            [SquirrelBoxMetadataNames.MetadataSection] = value
        };

    private static bool InvokeContains(MethodInfo method, IReadOnlyDictionary<string, object> metadata)
        => (bool)method.Invoke(null, [metadata])!;
}
