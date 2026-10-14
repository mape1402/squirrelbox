namespace SquirrelBox.Tests;

public sealed class EventSinkAndIdentityAccessorTests
{
    [Fact]
    public async Task Event_sink_buffers_recent_events_notifies_subscribers_and_ignores_subscriber_failures()
    {
        var sink = new InMemorySquirrelBoxEventSink();
        var received = new List<string>();
        using var subscription = sink.Subscribe((@event, _) =>
        {
            received.Add(@event.Name);
            return ValueTask.CompletedTask;
        });
        using var failingSubscription = sink.Subscribe((_, _) => throw new InvalidOperationException("listener failed"));

        for (var i = 0; i < 505; i++)
        {
            await sink.PublishAsync(new SquirrelBoxEvent
            {
                Name = $"event-{i}",
                OccurredOnUtc = DateTimeOffset.UtcNow
            });
        }

        subscription.Dispose();
        await sink.PublishAsync(new SquirrelBoxEvent
        {
            Name = "after-dispose",
            OccurredOnUtc = DateTimeOffset.UtcNow
        });

        var recent = sink.GetRecent(limit: 2);

        Assert.Equal(505, received.Count);
        Assert.DoesNotContain("after-dispose", received);
        Assert.Equal(["event-504", "after-dispose"], recent.Select(item => item.Name).ToArray());
        Assert.DoesNotContain(sink.GetRecent(600), item => item.Name == "event-0");
    }

    [Fact]
    public async Task Event_sink_validates_null_arguments()
    {
        var sink = new InMemorySquirrelBoxEventSink();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sink.PublishAsync(null).AsTask());
        Assert.Throws<ArgumentNullException>(() => sink.Subscribe(null));
    }

    [Fact]
    public void Identity_accessor_prepares_and_flows_current_identity()
    {
        var accessor = new AsyncLocalSquirrelBoxIdentityAccessor();
        var identity = CreateIdentity();

        accessor.Prepare();
        accessor.Current = identity;

        Assert.Same(identity, accessor.Current);
        Assert.Equal("key-1", accessor.Current.Operation.IdempotencyKey.Value);
    }

    internal static SquirrelBoxIdentity CreateIdentity()
        => new()
        {
            Operation = new SquirrelBoxOperationIdentity
            {
                IdempotencyKey = SquirrelBoxMetadataValue.Create(
                    SquirrelBoxMetadataNames.IdempotencyKey,
                    "key-1",
                    SquirrelBoxMetadataValueSource.Incoming),
                CorrelationId = SquirrelBoxMetadataValue.Create(
                    SquirrelBoxMetadataNames.CorrelationId,
                    "corr-1",
                    SquirrelBoxMetadataValueSource.Generated)
            },
            Attempt = new SquirrelBoxAttemptIdentity
            {
                AttemptId = SquirrelBoxMetadataValue.Create(
                    SquirrelBoxMetadataNames.AttemptId,
                    "attempt-1",
                    SquirrelBoxMetadataValueSource.Generated),
                TraceId = SquirrelBoxMetadataValue.Create(
                    SquirrelBoxMetadataNames.TraceId,
                    "trace-1",
                    SquirrelBoxMetadataValueSource.Generated)
            }
        };
}
