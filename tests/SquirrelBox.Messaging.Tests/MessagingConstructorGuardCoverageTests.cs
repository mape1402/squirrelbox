using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Reflection;
using Mule;
using NSubstitute;
using Pigeon.Messaging;
using Pigeon.Messaging.Consuming.Dispatching;
using Pigeon.Messaging.Contracts;
using Pigeon.Messaging.Producing;
using SquirrelBox.InMemory;
using SquirrelBox.Messaging.Pigeon;
using SquirrelBox.Mule;

namespace SquirrelBox.Messaging.Tests;

public sealed class MessagingConstructorGuardCoverageTests
{
    [Theory]
    [InlineData("inbox")]
    [InlineData("policyResolver")]
    [InlineData("policyRegistry")]
    [InlineData("metadataEnricher")]
    [InlineData("options")]
    public void InboxMessageService_requires_constructor_dependencies(string dependency)
    {
        using var provider = CreateProvider();

        Assert.Throws<ArgumentNullException>(() => new DefaultInboxMessageService(
            dependency == "inbox" ? null : provider.GetRequiredService<IInboxService>(),
            dependency == "policyResolver" ? null : provider.GetRequiredService<IInboxPolicyResolver>(),
            dependency == "policyRegistry" ? null : provider.GetRequiredService<IInboxMessagePolicyRegistry>(),
            dependency == "metadataEnricher" ? null : provider.GetRequiredService<ISquirrelBoxMessageMetadataEnricher>(),
            dependency == "options" ? null : provider.GetRequiredService<IOptions<SquirrelBoxMessagingOptions>>()));
    }

    [Theory]
    [InlineData("messages")]
    [InlineData("metadataEnricher")]
    [InlineData("scheduler")]
    [InlineData("inbox")]
    [InlineData("envelopeFactory")]
    [InlineData("options")]
    public void PigeonDecisionInterceptor_requires_constructor_dependencies(string dependency)
    {
        using var provider = CreateProvider();

        Assert.Throws<ArgumentNullException>(() => new SquirrelBoxPigeonDecisionInterceptor(
            dependency == "messages" ? null : provider.GetRequiredService<IInboxMessageService>(),
            dependency == "metadataEnricher" ? null : provider.GetRequiredService<ISquirrelBoxMessageMetadataEnricher>(),
            dependency == "scheduler" ? null : provider.GetRequiredService<IInboxMuleScheduler>(),
            dependency == "inbox" ? null : provider.GetRequiredService<IInboxService>(),
            dependency == "envelopeFactory" ? null : provider.GetRequiredService<IPigeonConsumeEnvelopeFactory>(),
            dependency == "options" ? null : provider.GetRequiredService<IOptions<SquirrelBoxPigeonOptions>>()));
    }

    [Theory]
    [InlineData("outbox")]
    [InlineData("inbox")]
    [InlineData("envelopeFactory")]
    [InlineData("serializer")]
    [InlineData("metadataEnricher")]
    [InlineData("options")]
    public void PigeonOutboxInterceptor_requires_constructor_dependencies(string dependency)
    {
        using var provider = CreateProvider();

        Assert.Throws<ArgumentNullException>(() => new SquirrelBoxPigeonOutboxInterceptor(
            dependency == "outbox" ? null : provider.GetRequiredService<IOutboxService>(),
            dependency == "inbox" ? null : provider.GetRequiredService<IInboxService>(),
            dependency == "envelopeFactory" ? null : provider.GetRequiredService<IPigeonPublishEnvelopeFactory>(),
            dependency == "serializer" ? null : provider.GetRequiredService<ISerializer>(),
            dependency == "metadataEnricher" ? null : provider.GetRequiredService<ISquirrelBoxMessageMetadataEnricher>(),
            dependency == "options" ? null : provider.GetRequiredService<IOptions<SquirrelBoxPigeonOptions>>()));
    }

    [Fact]
    public void PigeonExecutionInterceptor_requires_inbox()
    {
        Assert.Throws<ArgumentNullException>(() => new SquirrelBoxPigeonExecutionInterceptor(null));
    }

    [Fact]
    public void PigeonMuleAction_requires_consumer_invoker()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new SquirrelBoxPigeonMuleAction(Substitute.For<IInboxService>(), null));
    }

    [Theory]
    [InlineData("publisherInvoker")]
    [InlineData("outboxSerializer")]
    [InlineData("payloadTypes")]
    [InlineData("pigeonSerializer")]
    [InlineData("options")]
    public void PigeonOutboxPublisher_requires_constructor_dependencies(string dependency)
    {
        using var provider = CreateProvider();

        Assert.Throws<ArgumentNullException>(() => new SquirrelBoxPigeonOutboxPublisher(
            dependency == "publisherInvoker" ? null : provider.GetRequiredService<IPigeonPublisherInvoker>(),
            dependency == "outboxSerializer" ? null : provider.GetRequiredService<IOutboxEnvelopeSerializer>(),
            dependency == "payloadTypes" ? null : provider.GetRequiredService<IOutboxPayloadTypeNameResolver>(),
            dependency == "pigeonSerializer" ? null : provider.GetRequiredService<ISerializer>(),
            dependency == "options" ? null : provider.GetRequiredService<IOptions<SquirrelBoxPigeonOptions>>()));
    }

    [Theory]
    [InlineData("SquirrelBox.Messaging.InboxMessagePolicyBuilder", typeof(InboxMessagePolicy))]
    [InlineData("SquirrelBox.Messaging.InboxMessagePolicyProfileBuilder", typeof(IList<InboxMessagePolicy>))]
    public void Internal_policy_builders_require_their_models(string typeName, Type parameterType)
    {
        var type = typeof(InboxMessagePolicy).Assembly.GetType(typeName, throwOnError: true)!;
        var constructor = type.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [parameterType],
            modifiers: null)!;

        var exception = Assert.Throws<TargetInvocationException>(() => constructor.Invoke([null]));

        Assert.IsType<ArgumentNullException>(exception.InnerException);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSquirrelBox().UseInMemory();
        services.AddSquirrelBoxMessaging(options => options.ScanAssemblyContaining<MessagingConstructorGuardCoverageTests>());
        services.AddSingleton(Options.Create(new SquirrelBoxPigeonOptions()));
        services.AddSingleton<IInboxMuleScheduler, FakeInboxMuleScheduler>();
        services.AddSingleton<IPigeonConsumeEnvelopeFactory, FakeConsumeEnvelopeFactory>();
        services.AddSingleton<IPigeonPublishEnvelopeFactory, FakePublishEnvelopeFactory>();
        services.AddSingleton<IPigeonPublisherInvoker, FakePublisherInvoker>();
        services.AddSingleton<ISerializer, FakeSerializer>();
        return services.BuildServiceProvider();
    }

    private sealed class FakeInboxMuleScheduler : IInboxMuleScheduler
    {
        public ValueTask<Guid> EnqueueCurrentAsync<TPayload>(
            ActionKey key,
            TPayload payload,
            Action<EnqueueOptions> configure = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(Guid.NewGuid());
    }

    private sealed class FakeConsumeEnvelopeFactory : IPigeonConsumeEnvelopeFactory
    {
        public PigeonConsumeEnvelope Create(ConsumeContext context)
            => new();

        public ConsumeContext CreateContext(
            PigeonConsumeEnvelope envelope,
            IServiceProvider services,
            CancellationToken cancellationToken = default)
            => new();
    }

    private sealed class FakePublishEnvelopeFactory : IPigeonPublishEnvelopeFactory
    {
        public ValueTask<PigeonPublishEnvelope> CreateAsync(
            PublishContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new PigeonPublishEnvelope());
    }

    private sealed class FakePublisherInvoker : IPigeonPublisherInvoker
    {
        public ValueTask PublishAsync(
            PigeonPublishEnvelope envelope,
            CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
    }

    private sealed class FakeSerializer : ISerializer
    {
        public string Serialize(object payload)
            => "{}";

        public object Deserialize(string rawJson, Type targetType)
            => Activator.CreateInstance(targetType);
    }
}

public sealed class ConstructorGuardPolicyProfile : InboxMessagePolicyProfile
{
    public override void Configure(InboxMessagePolicyProfileBuilder builder)
        => builder.ForTopic("constructor-guards");
}
