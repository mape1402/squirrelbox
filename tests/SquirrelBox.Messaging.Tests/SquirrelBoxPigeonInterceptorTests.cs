using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Mule;
using Pigeon.Messaging.Consuming.Dispatching;
using Pigeon.Messaging.Contracts;
using SquirrelBox.InMemory;
using SquirrelBox.Messaging.Pigeon;
using SquirrelBox.Mule;

namespace SquirrelBox.Messaging.Tests;

public sealed class SquirrelBoxPigeonInterceptorTests
{
    [Fact]
    public async Task DecisionInterceptor_opens_inbox_and_continues_inline_consume()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(scope.ServiceProvider);

        var decision = await interceptor.InterceptAsync(context);

        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        Assert.Equal(PigeonConsumeDecision.Continue, decision.Decision);
        Assert.NotNull(inbox.Current);
        Assert.Equal("pigeon-key", inbox.Current.EffectiveIdempotencyKey);
        Assert.Equal("orders:1.2.0/billing/OrderMessage", inbox.Current.Entry.Operation);
        Assert.Equal("pigeon-key", context.ReplyMetadata["idempotency-key"]);
    }

    [Fact]
    public async Task ExecutionInterceptor_completes_inbox_after_successful_handler()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var decision = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var execution = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonExecutionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(scope.ServiceProvider);

        await decision.InterceptAsync(context);
        var entryId = scope.ServiceProvider.GetRequiredService<IInboxService>().Current.Entry.Id;

        await execution.InvokeAsync(context, (_, _) => ValueTask.CompletedTask);

        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        var entry = await inbox.GetAsync(entryId);

        Assert.Equal(InboxStatus.Completed, entry.Status);
        Assert.Null(inbox.Current);
    }

    [Fact]
    public async Task ExecutionInterceptor_fails_inbox_when_handler_throws()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var decision = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var execution = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonExecutionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(scope.ServiceProvider);

        await decision.InterceptAsync(context);
        var entryId = scope.ServiceProvider.GetRequiredService<IInboxService>().Current.Entry.Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            execution.InvokeAsync(
                context,
                (_, _) => throw new InvalidOperationException("boom")).AsTask());

        var entry = await scope.ServiceProvider.GetRequiredService<IInboxService>().GetAsync(entryId);
        Assert.Equal(InboxStatus.Failed, entry.Status);
        Assert.Equal("boom", entry.FailureDetails.ErrorMessage);
    }

    [Fact]
    public async Task DecisionInterceptor_defers_consume_and_schedules_pigeon_envelope()
    {
        using var provider = CreateProvider(options => options.ExecutionModeResolver = _ => InboxExecutionMode.Deferred);
        using var scope = provider.CreateScope();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(scope.ServiceProvider);

        var result = await interceptor.InterceptAsync(context);

        var scheduler = scope.ServiceProvider.GetRequiredService<FakeInboxMuleScheduler>();

        Assert.Equal(PigeonConsumeDecision.Defer, result.Decision);
        Assert.Single(scheduler.Payloads);
        var envelope = Assert.IsType<PigeonConsumeEnvelope>(scheduler.Payloads.Single());
        Assert.Equal("orders", envelope.Topic);
        Assert.Equal("pigeon-key", envelope.Metadata["idempotency-key"]);
        Assert.Null(scope.ServiceProvider.GetRequiredService<IInboxService>().Current);
    }

    [Fact]
    public async Task DecisionInterceptor_normalizes_empty_pigeon_version_for_deferred_envelope()
    {
        using var provider = CreateProvider(options => options.ExecutionModeResolver = _ => InboxExecutionMode.Deferred);
        using var scope = provider.CreateScope();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(scope.ServiceProvider, version: new SemanticVersion(0, 0, 0));

        await interceptor.InterceptAsync(context);

        var scheduler = scope.ServiceProvider.GetRequiredService<FakeInboxMuleScheduler>();
        var envelope = Assert.IsType<PigeonConsumeEnvelope>(scheduler.Payloads.Single());

        Assert.Equal(SemanticVersion.Default, envelope.Version);
        Assert.Equal("orders:1.0.0/billing/OrderMessage", scope.ServiceProvider.GetRequiredService<IInboxService>().LastContext.Entry.Operation);
    }

    [Fact]
    public async Task DecisionInterceptor_continues_without_opening_new_inbox_during_deferred_replay()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        await inbox.OpenOrContinueAsync(InboxOpenRequest.For("pigeon", "orders", "pigeon-key", new OrderMessage("order-1")));

        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(scope.ServiceProvider, ConsumeExecutionSource.DeferredReplay);

        var result = await interceptor.InterceptAsync(context);

        Assert.Equal(PigeonConsumeDecision.Continue, result.Decision);
        Assert.Empty(scope.ServiceProvider.GetRequiredService<FakeInboxMuleScheduler>().Payloads);
        Assert.Equal("pigeon-key", inbox.Current.EffectiveIdempotencyKey);
    }

    [Fact]
    public async Task ExecutionInterceptor_does_not_complete_inbox_during_deferred_replay()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        var opened = await inbox.OpenOrContinueAsync(InboxOpenRequest.For("pigeon", "orders", "pigeon-key", new OrderMessage("order-1")));
        var context = CreateContext(scope.ServiceProvider, ConsumeExecutionSource.DeferredReplay);
        var execution = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonExecutionInterceptor>(scope.ServiceProvider);

        await execution.InvokeAsync(context, (_, _) => ValueTask.CompletedTask);

        var entry = await inbox.GetAsync(opened.Entry.Id);
        Assert.Equal(InboxStatus.Started, entry.Status);
        Assert.NotNull(inbox.Current);
    }

    [Fact]
    public async Task PigeonMuleAction_continues_inbox_invokes_consumer_and_completes_entry()
    {
        using var provider = CreateProvider();
        Ulid entryId;

        using (var openScope = provider.CreateScope())
        {
            var inbox = openScope.ServiceProvider.GetRequiredService<IInboxService>();
            var opened = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
                source: "pigeon",
                operation: "orders",
                idempotencyKey: "pigeon-key",
                payload: new OrderMessage("order-1"),
                executionMode: InboxExecutionMode.Deferred));

            entryId = opened.Entry.Id;
            inbox.ReleaseCurrent();
        }

        using (var workerScope = provider.CreateScope())
        {
            var invoker = workerScope.ServiceProvider.GetRequiredService<FakePigeonConsumerInvoker>();
            var action = new SquirrelBoxPigeonMuleAction(
                workerScope.ServiceProvider.GetRequiredService<IInboxService>(),
                invoker);

            await action.ExecuteAsync(
                CreateMuleContext(workerScope.ServiceProvider, entryId),
                CancellationToken.None);
        }

        using var verifyScope = provider.CreateScope();
        var entry = await verifyScope.ServiceProvider.GetRequiredService<IInboxService>().GetAsync(entryId);
        var fakeInvoker = provider.GetRequiredService<FakePigeonConsumerInvoker>();

        Assert.Equal(InboxStatus.Completed, entry.Status);
        Assert.Single(fakeInvoker.Envelopes);
        Assert.Equal("orders", fakeInvoker.Envelopes.Single().Topic);
    }

    [Fact]
    public async Task PigeonMuleAction_restores_serialized_version_from_envelope_metadata()
    {
        using var provider = CreateProvider();
        Ulid entryId;

        using (var openScope = provider.CreateScope())
        {
            var inbox = openScope.ServiceProvider.GetRequiredService<IInboxService>();
            var opened = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
                source: "pigeon",
                operation: "orders",
                idempotencyKey: "pigeon-key",
                payload: new OrderMessage("order-1"),
                executionMode: InboxExecutionMode.Deferred));

            entryId = opened.Entry.Id;
            inbox.ReleaseCurrent();
        }

        using (var workerScope = provider.CreateScope())
        {
            var invoker = workerScope.ServiceProvider.GetRequiredService<FakePigeonConsumerInvoker>();
            var action = new SquirrelBoxPigeonMuleAction(
                workerScope.ServiceProvider.GetRequiredService<IInboxService>(),
                invoker);

            await action.ExecuteAsync(
                CreateMuleContext(
                    workerScope.ServiceProvider,
                    entryId,
                    payloadVersion: new SemanticVersion(0, 0, 0),
                    metadataVersion: "1.2.3"),
                CancellationToken.None);
        }

        var fakeInvoker = provider.GetRequiredService<FakePigeonConsumerInvoker>();

        Assert.Equal(SemanticVersion.Parse("1.2.3"), fakeInvoker.Envelopes.Single().Version);
    }

    private static ServiceProvider CreateProvider(Action<SquirrelBoxPigeonOptions> configure = null)
    {
        var services = new ServiceCollection();
        services.AddSquirrelBox().UseInMemory();
        services.AddSquirrelBoxMessaging();
        services.AddSingleton<FakeInboxMuleScheduler>();
        services.AddSingleton<IInboxMuleScheduler>(provider => provider.GetRequiredService<FakeInboxMuleScheduler>());
        services.AddSingleton<FakePigeonConsumerInvoker>();
        services.AddSingleton<IPigeonConsumerInvoker>(provider => provider.GetRequiredService<FakePigeonConsumerInvoker>());
        services.AddSingleton<IPigeonConsumeEnvelopeFactory, FakePigeonConsumeEnvelopeFactory>();
        services.AddSingleton(Options.Create(CreateOptions(configure)));
        return services.BuildServiceProvider();
    }

    private static SquirrelBoxPigeonOptions CreateOptions(Action<SquirrelBoxPigeonOptions> configure)
    {
        var options = new SquirrelBoxPigeonOptions();
        configure?.Invoke(options);
        return options;
    }

    private static ConsumeContext CreateContext(
        IServiceProvider services,
        ConsumeExecutionSource executionSource = ConsumeExecutionSource.BrokerDelivery,
        SemanticVersion? version = null)
        => new()
        {
            Services = services,
            Topic = "orders",
            Subscription = "billing",
            MessageVersion = version ?? SemanticVersion.Parse("1.2.0"),
            Message = new OrderMessage("order-1"),
            MessageType = typeof(OrderMessage),
            ExecutionSource = executionSource,
            RawMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["idempotency-key"] = "pigeon-key",
                ["message-id"] = "broker-id"
            }
        };

    private sealed record OrderMessage(string Id);

    private sealed class FakePigeonConsumeEnvelopeFactory : IPigeonConsumeEnvelopeFactory
    {
        public PigeonConsumeEnvelope Create(ConsumeContext context)
            => new()
            {
                Topic = context.Topic,
                Version = context.MessageVersion,
                Subscription = context.Subscription,
                PayloadType = context.MessageType.AssemblyQualifiedName,
                Payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(context.Message, context.MessageType),
                CreatedOnUtc = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string>(context.RawMetadata, StringComparer.OrdinalIgnoreCase)
            };

        public ConsumeContext CreateContext(
            PigeonConsumeEnvelope envelope,
            IServiceProvider services,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeInboxMuleScheduler : IInboxMuleScheduler
    {
        public List<object> Payloads { get; } = [];

        public ValueTask<Guid> EnqueueCurrentAsync<TPayload>(
            ActionKey key,
            TPayload payload,
            Action<EnqueueOptions> configure = null,
            CancellationToken cancellationToken = default)
        {
            Payloads.Add(payload);
            configure?.Invoke(new EnqueueOptions());
            return ValueTask.FromResult(Guid.NewGuid());
        }
    }

    private static MuleActionContext<PigeonConsumeEnvelope> CreateMuleContext(
        IServiceProvider services,
        Ulid entryId,
        SemanticVersion? payloadVersion = null,
        string metadataVersion = null)
    {
        var durableAction = new DurableAction
        {
            Key = SquirrelBoxPigeonMuleActionKeys.ConsumeKey,
            Metadata = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
            {
                [SquirrelBoxMuleMetadata.InboxEntryId] = entryId.ToString(),
                [SquirrelBoxMuleMetadata.IdempotencyKey] = "pigeon-key"
            })
        };

        var envelope = new PigeonConsumeEnvelope
        {
            Topic = "orders",
            Version = payloadVersion ?? SemanticVersion.Default,
            Subscription = "billing",
            PayloadType = typeof(OrderMessage).AssemblyQualifiedName,
            Payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new OrderMessage("order-1")),
            CreatedOnUtc = DateTimeOffset.UtcNow,
            Metadata = metadataVersion is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["squirrelbox-pigeon-version"] = metadataVersion
                }
        };

        return new MuleActionContext<PigeonConsumeEnvelope>(durableAction, services, envelope);
    }

    private sealed class FakePigeonConsumerInvoker : IPigeonConsumerInvoker
    {
        public List<PigeonConsumeEnvelope> Envelopes { get; } = [];

        public ValueTask InvokeAsync(
            PigeonConsumeEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            return ValueTask.CompletedTask;
        }
    }
}
