using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mule;
using Mule.InMemory;
using NSubstitute;
using SquirrelBox.InMemory;

namespace SquirrelBox.Mule.Tests;

public sealed class OutboxMuleSchedulerTests
{
    [Fact]
    public async Task ScheduleAsync_enqueues_mule_action_with_outbox_metadata()
    {
        var mule = Substitute.For<IMuleClient>();
        ActionKey capturedKey = default;
        EnqueueOptions capturedOptions = null;
        SquirrelBoxOutboxEnvelopeReference capturedPayload = null;
        mule.EnqueueAsync(
                Arg.Do<ActionKey>(key => capturedKey = key),
                Arg.Do<SquirrelBoxOutboxEnvelopeReference>(payload => capturedPayload = payload),
                Arg.Do<Action<EnqueueOptions>>(configure =>
                {
                    capturedOptions = new EnqueueOptions();
                    configure(capturedOptions);
                }),
                Arg.Any<CancellationToken>())
            .Returns(new Guid("22222222-2222-2222-2222-222222222222"));

        var envelope = new OutboxEnvelope
        {
            Id = Ulid.NewUlid(),
            Transport = "pigeon",
            Operation = "pigeon.publish",
            Destination = "orders",
            PayloadType = typeof(string).AssemblyQualifiedName,
            Payload = [1],
            CorrelationId = "trace-1"
        };

        var scheduler = new SquirrelBoxOutboxMuleScheduler(mule);
        await scheduler.ScheduleAsync(envelope);

        Assert.Equal(SquirrelBoxOutboxMuleActionKeys.PublishKey, capturedKey);
        Assert.Equal(envelope.Id.ToString(), capturedPayload.EnvelopeId);
        Assert.Equal("trace-1", capturedOptions.CorrelationId);
        Assert.Equal(envelope.Id.ToString(), capturedOptions.DeduplicationKey);
        Assert.Equal(envelope.Id.ToString(), capturedOptions.Metadata[SquirrelBoxMuleMetadata.OutboxEnvelopeId]);
    }

    [Fact]
    public async Task Hosted_mule_worker_publishes_outbox_envelope_end_to_end()
    {
        using var host = CreateHost();
        await host.StartAsync();

        Ulid envelopeId;
        using (var scope = host.Services.CreateScope())
        {
            var envelope = await scope.ServiceProvider.GetRequiredService<IOutboxService>()
                .EnqueueAsync(new OutboxEnqueueRequest
                {
                    Transport = "test",
                    Operation = "orders.created",
                    Destination = "orders",
                    Payload = new OutboxMessage("order-1"),
                    CorrelationId = "trace-outbox"
                });

            envelopeId = envelope.Id;
        }

        var publisher = host.Services.GetRequiredService<TestOutboxPublisher>();
        await publisher.WaitAsync();

        using (var scope = host.Services.CreateScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<IOutboxService>().GetAsync(envelopeId);
            Assert.Equal(OutboxStatus.Published, stored.Status);
        }

        await host.StopAsync();
        Assert.Equal(envelopeId, publisher.Published.Single().Id);
        Assert.Contains(host.Services.GetRequiredService<IInMemoryMule>().Actions, action => action.Status == DurableActionStatus.Completed);
    }

    private static IHost CreateHost()
        => Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.Configure<MuleSettings>(settings =>
                {
                    settings.DispatchInterval = TimeSpan.FromMilliseconds(50);
                    settings.RetryDelay = TimeSpan.FromMilliseconds(50);
                    settings.MaxAttempts = 1;
                });

                services.AddSingleton<TestOutboxPublisher>();
                services.AddSingleton<IOutboxTransportPublisher>(provider => provider.GetRequiredService<TestOutboxPublisher>());
                services.AddSquirrelBox().UseInMemory();
                services.AddSquirrelBoxMule();
                services.AddMule(mule => mule
                    .UseInMemory()
                    .AddActionsFromAssemblyContaining<SquirrelBoxOutboxMuleAction>());
            })
            .Build();

    private sealed record OutboxMessage(string Id);

    private sealed class TestOutboxPublisher : IOutboxTransportPublisher
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Transport => "test";

        public IList<OutboxEnvelope> Published { get; } = [];

        public ValueTask<OutboxPublishResult> PublishAsync(
            OutboxEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Published.Add(envelope);
            _completion.TrySetResult();
            return ValueTask.FromResult(OutboxPublishResult.Success);
        }

        public Task WaitAsync()
            => _completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }
}
