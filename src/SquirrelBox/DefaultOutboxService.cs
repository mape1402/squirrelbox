using Microsoft.Extensions.Options;

namespace SquirrelBox;

/// <summary>
/// Default implementation of <see cref="IOutboxService"/>.
/// </summary>
public sealed class DefaultOutboxService : IOutboxService
{
    private readonly IOutboxContextAccessor _contextAccessor;
    private readonly IOutboxProfileRegistry _profiles;
    private readonly IEnumerable<IOutboxDeferredScheduler> _schedulers;
    private readonly IEnumerable<IOutboxTransportPublisher> _publishers;
    private readonly ISquirrelBoxEventPublisher _events;
    private readonly IOutboxEnvelopeSerializer _serializer;
    private readonly IOutboxStore _store;
    private readonly OutboxOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultOutboxService"/> class.
    /// </summary>
    public DefaultOutboxService(
        IOptions<SquirrelBoxOptions> options,
        IOutboxContextAccessor contextAccessor,
        IOutboxProfileRegistry profiles,
        IOutboxEnvelopeSerializer serializer,
        IOutboxStore store,
        IEnumerable<IOutboxDeferredScheduler> schedulers,
        IEnumerable<IOutboxTransportPublisher> publishers,
        ISquirrelBoxEventPublisher events,
        TimeProvider timeProvider)
    {
        _options = options?.Value.Outbox ?? throw new ArgumentNullException(nameof(options));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _schedulers = schedulers ?? throw new ArgumentNullException(nameof(schedulers));
        _publishers = publishers ?? throw new ArgumentNullException(nameof(publishers));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <inheritdoc />
    public OutboxContext Current => _contextAccessor.Current;

    /// <inheritdoc />
    public async ValueTask<OutboxEnvelope> EnqueueAsync(
        OutboxEnqueueRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _timeProvider.GetUtcNow();
        var profile = _profiles.Resolve(request);
        var envelope = profile.CreateEnvelope(request, new OutboxProfileContext(_serializer, _options, now));

        ValidateEnvelope(envelope);
        await _store.AddAsync(envelope, cancellationToken);
        await PublishEventAsync(SquirrelBoxEventNames.OutboxEnqueued, envelope, cancellationToken);

        var schedulers = _schedulers.ToArray();
        if (schedulers.Length == 0 && _options.RequireDeferredScheduler)
            throw new InvalidOperationException("SquirrelBox outbox requires an IOutboxDeferredScheduler. Add SquirrelBox.Mule to enable durable outbox publication.");

        foreach (var scheduler in schedulers)
        {
            await scheduler.ScheduleAsync(envelope, cancellationToken);
            await PublishEventAsync(SquirrelBoxEventNames.DeferredScheduled, envelope, cancellationToken);
        }

        return envelope;
    }

    /// <inheritdoc />
    public async ValueTask<OutboxPublishResult> PublishAsync(
        Ulid envelopeId,
        CancellationToken cancellationToken = default)
    {
        var envelope = await _store.GetAsync(envelopeId, cancellationToken);
        if (envelope.Status == OutboxStatus.Published || envelope.Status == OutboxStatus.Discarded)
            return OutboxPublishResult.Success;

        var previous = _contextAccessor.Current;
        _contextAccessor.Current = new OutboxContext(envelope, previous);

        try
        {
            var now = _timeProvider.GetUtcNow();
            await _store.MarkPublishingAsync(envelope.Id, now, cancellationToken);
            envelope.Status = OutboxStatus.Publishing;
            envelope.PublishingOnUtc = now;
            envelope.UpdatedOnUtc = now;
            await PublishEventAsync(SquirrelBoxEventNames.OutboxPublishing, envelope, cancellationToken);
            await PublishEventAsync(SquirrelBoxEventNames.DeferredStarted, envelope, cancellationToken);

            var publisher = ResolvePublisher(envelope.Transport);
            var result = await publisher.PublishAsync(envelope, cancellationToken);
            if (!result.Succeeded)
            {
                await MarkFailedAsync(envelope, result.Failure, cancellationToken);
                return result;
            }

            var publishedOnUtc = _timeProvider.GetUtcNow();
            await _store.MarkPublishedAsync(envelope.Id, publishedOnUtc, cancellationToken);
            envelope.Status = OutboxStatus.Published;
            envelope.PublishedOnUtc = publishedOnUtc;
            envelope.UpdatedOnUtc = publishedOnUtc;
            await PublishEventAsync(SquirrelBoxEventNames.OutboxPublished, envelope, cancellationToken);
            await PublishEventAsync(SquirrelBoxEventNames.DeferredCompleted, envelope, cancellationToken);
            return OutboxPublishResult.Success;
        }
        catch (Exception exception)
        {
            var failure = OutboxFailure.FromException(exception);
            await MarkFailedAsync(envelope, failure, cancellationToken);
            throw;
        }
        finally
        {
            _contextAccessor.Current = previous;
        }
    }

    /// <inheritdoc />
    public ValueTask<OutboxEnvelope> GetAsync(
        Ulid envelopeId,
        CancellationToken cancellationToken = default)
        => _store.GetAsync(envelopeId, cancellationToken);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<OutboxEnvelope>> QueryAsync(
        OutboxQuery query,
        CancellationToken cancellationToken = default)
        => _store.QueryAsync(query ?? new OutboxQuery(), cancellationToken);

    private async ValueTask MarkFailedAsync(
        OutboxEnvelope envelope,
        OutboxFailure failure,
        CancellationToken cancellationToken)
    {
        var failedOnUtc = _timeProvider.GetUtcNow();
        var nextAttemptOnUtc = failedOnUtc.Add(_options.RetryDelay);
        await _store.MarkFailedAsync(envelope.Id, failure, failedOnUtc, nextAttemptOnUtc, cancellationToken);
        envelope.Status = OutboxStatus.Failed;
        envelope.Attempts++;
        envelope.Failure = failure;
        envelope.NextAttemptOnUtc = nextAttemptOnUtc;
        envelope.UpdatedOnUtc = failedOnUtc;
        await PublishEventAsync(SquirrelBoxEventNames.OutboxFailed, envelope, cancellationToken);
        await PublishEventAsync(SquirrelBoxEventNames.DeferredFailed, envelope, cancellationToken);
    }

    private IOutboxTransportPublisher ResolvePublisher(string transport)
    {
        var publisher = _publishers.FirstOrDefault(candidate =>
            string.Equals(candidate.Transport, transport, StringComparison.OrdinalIgnoreCase));

        return publisher ?? throw new InvalidOperationException(
            $"No SquirrelBox outbox publisher is registered for transport '{transport}'.");
    }

    private ValueTask PublishEventAsync(
        string name,
        OutboxEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var @event = new SquirrelBoxEvent
        {
            Name = name,
            Category = name.StartsWith("Deferred", StringComparison.Ordinal) ? "Deferred" : "Outbox",
            SubjectId = envelope.Id.ToString(),
            CorrelationId = envelope.CorrelationId,
            Status = envelope.Status.ToString(),
            OccurredOnUtc = _timeProvider.GetUtcNow()
        };

        @event.Metadata["transport"] = envelope.Transport;
        @event.Metadata["operation"] = envelope.Operation;
        @event.Metadata["destination"] = envelope.Destination;
        return _events.PublishAsync(@event, cancellationToken);
    }

    private static void ValidateEnvelope(OutboxEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.Transport);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.Operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.PayloadType);

        if (envelope.Payload is not { Length: > 0 })
            throw new InvalidOperationException("Outbox envelopes must contain a serialized payload.");
    }
}
