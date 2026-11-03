using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Mule;
using Mule.InMemory;
using Pigeon.Messaging.Consuming.Dispatching;
using Pigeon.Messaging.Consuming.Management;
using Pigeon.Messaging.Contracts;
using Pigeon.Messaging.InMemory;
using Pigeon.Messaging.Producing;
using SquirrelBox.InMemory;
using SquirrelBox.Messaging.Pigeon;
using SquirrelBox.Mule;
using Ulid = System.Ulid;

namespace SquirrelBox.Messaging.Tests;

public sealed class SquirrelBoxPigeonCallbackOutboxE2ETests
{
    private const string RequestTopic = "squirrelbox.callback.requests";
    private const string ReplyTopic = "squirrelbox.callback.replies";

    [Fact]
    public async Task Callback_is_durable_before_inbox_completion_and_completed_duplicate_is_acknowledged()
    {
        await using var provider = CreateProvider(immediateDispatch: false);
        await StartHostedServicesAsync(provider);

        try
        {
            var request = new CallbackRequest("order-1", "north");
            using var scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IProducer>()
                .PublishAsync(request, RequestTopic);

            var broker = provider.GetRequiredService<IInMemoryBroker>();
            var probe = provider.GetRequiredService<CallbackProbe>();
            await WaitUntilAsync(() => probe.Executions == 1 &&
                                       broker.Deliveries.Count == 1 &&
                                       broker.Deliveries.Single().Completed);

            var envelope = Assert.Single(await scope.ServiceProvider
                .GetRequiredService<IOutboxStore>()
                .QueryAsync(new OutboxQuery { Transport = "pigeon" }));
            var inboxEntry = Assert.Single(await scope.ServiceProvider
                .GetRequiredService<IInboxDiagnosticsStore>()
                .QueryAsync(new InboxQuery { Source = "pigeon" }));
            var muleAction = Assert.Single(provider.GetRequiredService<IInMemoryMule>().Actions);

            Assert.Equal(InboxStatus.Completed, inboxEntry.Status);
            Assert.Equal(OutboxStatus.Pending, envelope.Status);
            Assert.Equal(DurableActionStatus.Pending, muleAction.Status);
            Assert.Equal(envelope.Id.ToString(), muleAction.DeduplicationKey);
            Assert.Empty(probe.Replies);
            Assert.True(SquirrelBoxMessageMetadata.TryReadFrom(
                envelope.Metadata,
                out var callbackMetadata));
            Assert.Equal(inboxEntry.IdempotencyKey, callbackMetadata.IdempotencyKey);
            Assert.Equal(inboxEntry.CorrelationId, callbackMetadata.CorrelationId);
            Assert.Equal(inboxEntry.LastAttemptId, callbackMetadata.AttemptId);
            Assert.Equal(inboxEntry.LastTraceId, callbackMetadata.TraceId);
            Assert.Equal(inboxEntry.CorrelationId, envelope.CorrelationId);
            Assert.Equal(inboxEntry.LastTraceId, envelope.TraceId);

            await scope.ServiceProvider.GetRequiredService<IProducer>()
                .PublishAsync(request, RequestTopic);

            var observation = await probe.Decision.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => broker.Deliveries.Count == 2 &&
                                       broker.Deliveries.All(delivery => delivery.Completed));

            Assert.Equal(1, probe.Executions);
            Assert.Equal(PigeonConsumeDecision.AckAndSkip, observation.Result.Decision);
            Assert.Equal(PigeonConsumeOutcomeKind.AlreadyProcessed, observation.Result.Outcome?.Kind);
            Assert.Single(await scope.ServiceProvider.GetRequiredService<IOutboxStore>()
                .QueryAsync(new OutboxQuery { Transport = "pigeon" }));
            Assert.Single(provider.GetRequiredService<IInMemoryMule>().Actions);
        }
        finally
        {
            await StopHostedServicesAsync(provider);
        }
    }

    [Fact]
    public async Task Mule_retries_failed_callback_publish_until_outbox_is_published()
    {
        await using var provider = CreateProvider(immediateDispatch: true, failFirstDurablePublish: true);
        await StartHostedServicesAsync(provider);

        try
        {
            var request = new CallbackRequest("order-2", "south");
            using var scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IProducer>()
                .PublishAsync(request, RequestTopic);

            var probe = provider.GetRequiredService<CallbackProbe>();
            var invoker = provider.GetRequiredService<FailOncePigeonPublisherInvoker>();
            await WaitUntilAsync(() => probe.Replies.Count == 1 && invoker.Attempts >= 2);

            var envelope = Assert.Single(await scope.ServiceProvider
                .GetRequiredService<IOutboxStore>()
                .QueryAsync(new OutboxQuery { Transport = "pigeon" }));
            var inboxEntry = Assert.Single(await scope.ServiceProvider
                .GetRequiredService<IInboxDiagnosticsStore>()
                .QueryAsync(new InboxQuery { Source = "pigeon" }));
            var attempts = await scope.ServiceProvider
                .GetRequiredService<IOutboxAttemptDiagnosticsStore>()
                .QueryAttemptsAsync(new OutboxAttemptQuery { OutboxEnvelopeId = envelope.Id });

            Assert.Equal(InboxStatus.Completed, inboxEntry.Status);
            Assert.Equal(OutboxStatus.Published, envelope.Status);
            Assert.Equal(2, invoker.Attempts);
            Assert.Contains(attempts, attempt => attempt.State == OutboxAttemptState.Failed);
            Assert.Contains(attempts, attempt => attempt.State == OutboxAttemptState.Published);
            Assert.Equal(DurableActionStatus.Completed,
                Assert.Single(provider.GetRequiredService<IInMemoryMule>().Actions).Status);

            await scope.ServiceProvider.GetRequiredService<IProducer>()
                .PublishAsync(request, RequestTopic);
            await probe.Decision.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => provider.GetRequiredService<IInMemoryBroker>().Deliveries.Count >= 3);

            Assert.Equal(1, probe.Executions);
            Assert.Single(probe.Replies);
            Assert.Single(await scope.ServiceProvider.GetRequiredService<IOutboxStore>()
                .QueryAsync(new OutboxQuery { Transport = "pigeon" }));
        }
        finally
        {
            await StopHostedServicesAsync(provider);
        }
    }

    [Fact]
    public async Task Outbox_persistence_failure_marks_inline_inbox_as_failed()
    {
        await using var provider = CreateProvider(
            immediateDispatch: false,
            failOutboxPersistence: true);
        using var scope = provider.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        var opened = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            "pigeon",
            "callback-test",
            "callback-key",
            new CallbackRequest("order-3", "west")));
        var interceptor = ActivatorUtilities
            .CreateInstance<SquirrelBoxPigeonExecutionInterceptor>(scope.ServiceProvider);
        var context = new ConsumeContext
        {
            Services = scope.ServiceProvider,
            Topic = RequestTopic,
            Subscription = "callback-worker",
            Message = new CallbackRequest("order-3", "west"),
            MessageType = typeof(CallbackRequest)
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => interceptor
            .InvokeAsync(
                context,
                async (_, cancellationToken) => await scope.ServiceProvider
                    .GetRequiredService<IProducer>()
                    .PublishAsync(
                        new CallbackReply("order-3", "accepted"),
                        ReplyTopic,
                        SemanticVersion.Default,
                        cancellationToken),
                CancellationToken.None)
            .AsTask());

        var entry = await inbox.GetAsync(opened.Entry.Id);
        Assert.Equal("planned outbox persistence failure", exception.Message);
        Assert.Equal(InboxStatus.Failed, entry.Status);
        Assert.Contains("planned outbox persistence failure", entry.Failure);
        Assert.Null(inbox.Current);
    }

    private static ServiceProvider CreateProvider(
        bool immediateDispatch,
        bool failFirstDurablePublish = false,
        bool failOutboxPersistence = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<CallbackProbe>();
        services.AddScoped<IConsumeDecisionObserver, CallbackDecisionObserver>();
        services.Configure<MuleSettings>(settings =>
        {
            settings.ImmediateDispatch = immediateDispatch;
            settings.DispatchInterval = immediateDispatch
                ? TimeSpan.FromMilliseconds(20)
                : TimeSpan.FromHours(1);
            settings.RetryDelay = TimeSpan.FromMilliseconds(20);
            settings.MaxAttempts = 3;
            settings.CleanupMode = MuleCleanupMode.Disabled;
        });
        services.AddSquirrelBox().UseInMemory();
        services.AddMule(mule => mule
            .UseInMemory()
            .AddActionsFromAssemblyContaining<SquirrelBoxOutboxMuleAction>());
        services.AddSquirrelBoxMessaging(options =>
            options.ScanAssemblyContaining<CallbackInboxPolicyProfile>());

        var pigeon = services.AddPigeon(CreateConfiguration(), settings =>
        {
            settings.ConfigureConsumerExecution(execution =>
                execution.AcknowledgementMode = MessageAcknowledgementMode.OnHandlerSuccess);
            settings.UseInMemoryBroker();
        });
        pigeon.AddConsumeHandler<CallbackRequest>(
            RequestTopic,
            SemanticVersion.Default,
            "callback-worker",
            async (context, message) =>
            {
                context.Services.GetRequiredService<CallbackProbe>().RecordExecution();
                await context.Services.GetRequiredService<IProducer>()
                    .PublishAsync(
                        new CallbackReply(message.OrderId, "accepted"),
                        ReplyTopic,
                        SemanticVersion.Default,
                        context.CancellationToken);
            });
        pigeon.AddConsumeHandler<CallbackReply>(
            ReplyTopic,
            SemanticVersion.Default,
            "callback-client",
            (context, message) =>
            {
                context.Services.GetRequiredService<CallbackProbe>().RecordReply(message);
                return Task.CompletedTask;
            });

        services.AddSquirrelBoxPigeon(options =>
        {
            options.EnableOutbox = true;
            options.OutboxPredicate = context =>
                string.Equals(context.Route?.Topic, ReplyTopic, StringComparison.Ordinal);
        });

        if (failFirstDurablePublish)
            DecoratePublisherInvoker(services);

        if (failOutboxPersistence)
        {
            services.Replace(ServiceDescriptor.Singleton<IOutboxStore>(
                new FailingOutboxStore()));
        }

        return services.BuildServiceProvider();
    }

    private static void DecoratePublisherInvoker(IServiceCollection services)
    {
        var descriptor = services.Last(candidate =>
            candidate.ServiceType == typeof(IPigeonPublisherInvoker));
        var implementationType = descriptor.ImplementationType
            ?? throw new InvalidOperationException("Pigeon publisher invoker registration has no implementation type.");

        services.Remove(descriptor);
        services.AddSingleton(provider => new InnerPigeonPublisherInvoker(
            (IPigeonPublisherInvoker)ActivatorUtilities.CreateInstance(provider, implementationType)));
        services.AddSingleton<FailOncePigeonPublisherInvoker>();
        services.AddSingleton<IPigeonPublisherInvoker>(provider =>
            provider.GetRequiredService<FailOncePigeonPublisherInvoker>());
    }

    private static IConfiguration CreateConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Pigeon:Domain"] = "SquirrelBox.CallbackOutbox.Tests"
            })
            .Build();

    private static async Task StartHostedServicesAsync(IServiceProvider provider)
    {
        foreach (var service in provider.GetServices<IHostedService>())
            await service.StartAsync(CancellationToken.None);
    }

    private static async Task StopHostedServicesAsync(IServiceProvider provider)
    {
        foreach (var service in provider.GetServices<IHostedService>().Reverse())
            await service.StopAsync(CancellationToken.None);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < timeout)
        {
            if (condition())
                return;

            await Task.Delay(20);
        }

        throw new TimeoutException("The callback outbox scenario did not reach the expected state.");
    }

    public sealed record CallbackRequest(string OrderId, string Region);

    public sealed record CallbackReply(string OrderId, string Status);

    public sealed class CallbackInboxPolicyProfile : InboxMessagePolicyProfile
    {
        public override void Configure(InboxMessagePolicyProfileBuilder builder)
            => builder.ForTopic(RequestTopic);
    }

    private sealed class CallbackProbe
    {
        private readonly List<CallbackReply> _replies = [];
        private int _executions;

        public int Executions => Volatile.Read(ref _executions);

        public IReadOnlyList<CallbackReply> Replies
        {
            get
            {
                lock (_replies)
                    return _replies.ToArray();
            }
        }

        public TaskCompletionSource<ConsumeDecisionObservation> Decision { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void RecordExecution() => Interlocked.Increment(ref _executions);

        public void RecordReply(CallbackReply reply)
        {
            lock (_replies)
                _replies.Add(reply);
        }
    }

    private sealed class CallbackDecisionObserver(CallbackProbe probe) : IConsumeDecisionObserver
    {
        public ValueTask ObserveAsync(
            ConsumeDecisionObservation observation,
            CancellationToken cancellationToken = default)
        {
            probe.Decision.TrySetResult(observation);
            return ValueTask.CompletedTask;
        }
    }

    private sealed record InnerPigeonPublisherInvoker(IPigeonPublisherInvoker Value);

    private sealed class FailOncePigeonPublisherInvoker(
        InnerPigeonPublisherInvoker inner) : IPigeonPublisherInvoker
    {
        private int _attempts;

        public int Attempts => Volatile.Read(ref _attempts);

        public ValueTask PublishAsync(
            PigeonPublishEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _attempts) == 1)
                throw new InvalidOperationException("planned broker publication failure");

            return inner.Value.PublishAsync(envelope, cancellationToken);
        }
    }

    private sealed class FailingOutboxStore : IOutboxStore
    {
        public ValueTask AddAsync(
            OutboxEnvelope envelope,
            CancellationToken cancellationToken = default)
            => ValueTask.FromException(new InvalidOperationException(
                "planned outbox persistence failure"));

        public ValueTask<OutboxEnvelope> GetAsync(
            Ulid envelopeId,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask MarkPublishingAsync(
            Ulid envelopeId,
            DateTimeOffset publishingOnUtc,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask MarkPublishedAsync(
            Ulid envelopeId,
            DateTimeOffset publishedOnUtc,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask MarkFailedAsync(
            Ulid envelopeId,
            OutboxFailure failure,
            DateTimeOffset failedOnUtc,
            DateTimeOffset? nextAttemptOnUtc,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask MarkDiscardedAsync(
            Ulid envelopeId,
            DateTimeOffset discardedOnUtc,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public ValueTask<IReadOnlyList<OutboxEnvelope>> QueryAsync(
            OutboxQuery query,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
