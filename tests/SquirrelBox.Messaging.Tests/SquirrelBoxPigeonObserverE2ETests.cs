using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

namespace SquirrelBox.Messaging.Tests;

public sealed class SquirrelBoxPigeonObserverE2ETests
{
    [Fact]
    public async Task InMemory_broker_observer_receives_terminal_outcome_and_grouped_metadata_before_settlement()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ObserverState>();
        services.AddScoped<IConsumeDecisionObserver, CapturingDecisionObserver>();
        services.AddSquirrelBox().UseInMemory();
        services.AddMule(mule => mule.UseInMemory());
        services.AddSquirrelBoxMessaging(options =>
            options.ScanAssemblyContaining<SquirrelBoxPigeonObserverE2ETests>());

        var executions = 0;
        var pigeon = services.AddPigeon(CreateConfiguration(), settings =>
        {
            settings.ConfigureConsumerExecution(execution =>
                execution.AcknowledgementMode = MessageAcknowledgementMode.OnHandlerSuccess);
            settings.UseInMemoryBroker();
        });
        pigeon.AddConsumeHandler<ObservedMessage>(
            "orders",
            SemanticVersion.Default,
            "billing",
            (_, _) =>
            {
                Interlocked.Increment(ref executions);
                return Task.CompletedTask;
            });
        services.AddSquirrelBoxPigeon();

        await using var provider = services.BuildServiceProvider();
        await StartHostedServicesAsync(provider);
        try
        {
            using var scope = provider.CreateScope();
            var producer = scope.ServiceProvider.GetRequiredService<IProducer>();
            var broker = provider.GetRequiredService<IInMemoryBroker>();
            await producer.PublishAsync(new ObservedMessage("one"), "orders");
            await WaitUntilAsync(() => broker.Deliveries.Count == 1 && broker.Deliveries.Single().Completed);

            await producer.PublishAsync(new ObservedMessage("one"), "orders");
            var observation = await provider.GetRequiredService<ObserverState>()
                .Observed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => broker.Deliveries.Count == 2 && broker.Deliveries.All(delivery => delivery.Completed));

            Assert.Equal(1, executions);
            Assert.Equal(PigeonConsumeDecision.AckAndSkip, observation.Result.Decision);
            Assert.Equal(PigeonConsumeOutcomeKind.AlreadyProcessed, observation.Result.Outcome.Kind);
            Assert.Null(observation.Result.Outcome.Failure);
            Assert.True(SquirrelBoxMessageMetadata.TryReadFrom(
                observation.Result.Metadata,
                out var resultMetadata));
            Assert.True(SquirrelBoxMessageMetadata.TryReadFrom(
                observation.Context.ReplyMetadata,
                out var replyMetadata));
            Assert.False(string.IsNullOrWhiteSpace(resultMetadata.IdempotencyKey));
            Assert.Equal(resultMetadata.CorrelationId, replyMetadata.CorrelationId);
            Assert.Equal(resultMetadata.AttemptId, replyMetadata.AttemptId);
            Assert.Equal(resultMetadata.TraceId, replyMetadata.TraceId);
        }
        finally
        {
            await StopHostedServicesAsync(provider);
        }
    }

    private static IConfiguration CreateConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Pigeon:Domain"] = "SquirrelBox.Tests"
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
        var timeout = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < timeout)
        {
            if (condition())
                return;

            await Task.Delay(25);
        }

        throw new TimeoutException("The expected Pigeon delivery state was not reached.");
    }

    private sealed record ObservedMessage(string Value);

    private sealed class ObserverState
    {
        public TaskCompletionSource<ConsumeDecisionObservation> Observed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class CapturingDecisionObserver(ObserverState state) : IConsumeDecisionObserver
    {
        public ValueTask ObserveAsync(
            ConsumeDecisionObservation observation,
            CancellationToken cancellationToken = default)
        {
            state.Observed.TrySetResult(observation);
            return ValueTask.CompletedTask;
        }
    }

}
