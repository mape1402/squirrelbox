using System.Collections.Concurrent;

namespace SquirrelBox;

/// <summary>
/// In-memory event sink used by dashboards and tests to receive live SquirrelBox events.
/// </summary>
public sealed class InMemorySquirrelBoxEventSink : ISquirrelBoxEventSink
{
    private const int MaxBufferedEvents = 500;
    private readonly ConcurrentQueue<SquirrelBoxEvent> _recent = new();
    private readonly ConcurrentDictionary<Guid, Func<SquirrelBoxEvent, CancellationToken, ValueTask>> _subscribers = new();

    /// <inheritdoc />
    public async ValueTask PublishAsync(SquirrelBoxEvent @event, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        _recent.Enqueue(@event);
        while (_recent.Count > MaxBufferedEvents && _recent.TryDequeue(out _))
        {
        }

        foreach (var subscriber in _subscribers.Values)
            await subscriber(@event, cancellationToken);
    }

    /// <inheritdoc />
    public IDisposable Subscribe(Func<SquirrelBoxEvent, CancellationToken, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var id = Guid.NewGuid();
        _subscribers[id] = handler;
        return new Subscription(_subscribers, id);
    }

    /// <inheritdoc />
    public IReadOnlyList<SquirrelBoxEvent> GetRecent(int limit = 100)
        => _recent.Reverse().Take(Math.Max(1, limit)).Reverse().ToArray();

    private sealed class Subscription : IDisposable
    {
        private readonly ConcurrentDictionary<Guid, Func<SquirrelBoxEvent, CancellationToken, ValueTask>> _subscribers;
        private readonly Guid _id;

        public Subscription(
            ConcurrentDictionary<Guid, Func<SquirrelBoxEvent, CancellationToken, ValueTask>> subscribers,
            Guid id)
        {
            _subscribers = subscribers;
            _id = id;
        }

        public void Dispose()
            => _subscribers.TryRemove(_id, out _);
    }
}
