using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.InMemory;

namespace SquirrelBox.Tests;

public sealed class OutboxServiceTests
{
    [Fact]
    public async Task EnqueueAsync_persists_envelope_and_emits_event()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        var envelope = await outbox.EnqueueAsync(new OutboxEnqueueRequest
        {
            Transport = "test",
            Operation = "orders.created",
            Destination = "orders",
            Payload = new TestMessage("order-1"),
            CorrelationId = "trace-1"
        });

        var stored = await outbox.GetAsync(envelope.Id);
        var events = provider.GetRequiredService<ISquirrelBoxEventSink>().GetRecent();

        Assert.Equal(OutboxStatus.Pending, stored.Status);
        Assert.Equal("test", stored.Transport);
        Assert.Equal("orders.created", stored.Operation);
        Assert.Contains(events, item => item.Name == SquirrelBoxEventNames.OutboxEnqueued &&
                                       item.SubjectId == envelope.Id.ToString());
    }

    [Fact]
    public async Task PublishAsync_delivers_with_registered_transport_publisher_and_updates_status()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        var envelope = await outbox.EnqueueAsync(new OutboxEnqueueRequest
        {
            Transport = "test",
            Operation = "orders.created",
            Destination = "orders",
            Payload = new TestMessage("order-2")
        });

        var result = await outbox.PublishAsync(envelope.Id);
        var stored = await outbox.GetAsync(envelope.Id);
        var publisher = provider.GetRequiredService<TestOutboxPublisher>();
        var events = provider.GetRequiredService<ISquirrelBoxEventSink>().GetRecent();

        Assert.True(result.Succeeded);
        Assert.Equal(OutboxStatus.Published, stored.Status);
        Assert.Equal(envelope.Id, publisher.Published.Single().Id);
        Assert.Contains(events, item => item.Name == SquirrelBoxEventNames.OutboxPublished &&
                                       item.SubjectId == envelope.Id.ToString());
    }

    [Fact]
    public async Task PublishAsync_marks_failed_when_transport_publisher_fails()
    {
        using var provider = CreateProvider(fail: true);
        using var scope = provider.CreateScope();

        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        var envelope = await outbox.EnqueueAsync(new OutboxEnqueueRequest
        {
            Transport = "test",
            Operation = "orders.created",
            Destination = "orders",
            Payload = new TestMessage("order-3")
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => outbox.PublishAsync(envelope.Id).AsTask());
        var stored = await outbox.GetAsync(envelope.Id);

        Assert.Equal(OutboxStatus.Failed, stored.Status);
        Assert.Equal(1, stored.Attempts);
        Assert.Contains("planned", stored.Failure.Details);
    }

    private static ServiceProvider CreateProvider(bool fail = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TestOutboxPublisher(fail));
        services.AddSingleton<IOutboxTransportPublisher>(provider => provider.GetRequiredService<TestOutboxPublisher>());
        services.AddSquirrelBox().UseInMemory();
        return services.BuildServiceProvider();
    }

    private sealed record TestMessage(string Id);

    private sealed class TestOutboxPublisher : IOutboxTransportPublisher
    {
        private readonly bool _fail;

        public TestOutboxPublisher(bool fail)
        {
            _fail = fail;
        }

        public string Transport => "test";

        public IList<OutboxEnvelope> Published { get; } = [];

        public ValueTask<OutboxPublishResult> PublishAsync(
            OutboxEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            if (_fail)
                throw new InvalidOperationException("planned publisher failure");

            Published.Add(envelope);
            return ValueTask.FromResult(OutboxPublishResult.Success);
        }
    }
}
