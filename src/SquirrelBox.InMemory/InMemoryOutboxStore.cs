using System.Collections.Concurrent;

namespace SquirrelBox.InMemory;

/// <summary>
/// In-memory implementation of <see cref="IOutboxStore"/> for tests and local scenarios.
/// </summary>
public sealed class InMemoryOutboxStore : IOutboxStore
{
    private readonly ConcurrentDictionary<Ulid, OutboxEnvelope> _envelopes = new();

    /// <inheritdoc />
    public ValueTask AddAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!_envelopes.TryAdd(envelope.Id, envelope))
            throw new InvalidOperationException($"Outbox envelope '{envelope.Id}' already exists.");

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<OutboxEnvelope> GetAsync(Ulid envelopeId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(GetExisting(envelopeId));

    /// <inheritdoc />
    public ValueTask MarkPublishingAsync(
        Ulid envelopeId,
        DateTimeOffset publishingOnUtc,
        CancellationToken cancellationToken = default)
    {
        var envelope = GetExisting(envelopeId);
        envelope.Status = OutboxStatus.Publishing;
        envelope.PublishingOnUtc = publishingOnUtc;
        envelope.UpdatedOnUtc = publishingOnUtc;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask MarkPublishedAsync(
        Ulid envelopeId,
        DateTimeOffset publishedOnUtc,
        CancellationToken cancellationToken = default)
    {
        var envelope = GetExisting(envelopeId);
        envelope.Status = OutboxStatus.Published;
        envelope.PublishedOnUtc = publishedOnUtc;
        envelope.UpdatedOnUtc = publishedOnUtc;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask MarkFailedAsync(
        Ulid envelopeId,
        OutboxFailure failure,
        DateTimeOffset failedOnUtc,
        DateTimeOffset? nextAttemptOnUtc,
        CancellationToken cancellationToken = default)
    {
        var envelope = GetExisting(envelopeId);
        envelope.Status = OutboxStatus.Failed;
        envelope.Attempts++;
        envelope.Failure = failure;
        envelope.NextAttemptOnUtc = nextAttemptOnUtc;
        envelope.UpdatedOnUtc = failedOnUtc;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask MarkDiscardedAsync(
        Ulid envelopeId,
        DateTimeOffset discardedOnUtc,
        CancellationToken cancellationToken = default)
    {
        var envelope = GetExisting(envelopeId);
        envelope.Status = OutboxStatus.Discarded;
        envelope.UpdatedOnUtc = discardedOnUtc;
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<OutboxEnvelope>> QueryAsync(
        OutboxQuery query,
        CancellationToken cancellationToken = default)
    {
        query ??= new OutboxQuery();

        var result = _envelopes.Values
            .Where(envelope => query.Status is null || envelope.Status == query.Status)
            .Where(envelope => string.IsNullOrWhiteSpace(query.Transport) ||
                               string.Equals(envelope.Transport, query.Transport, StringComparison.OrdinalIgnoreCase))
            .Where(envelope => string.IsNullOrWhiteSpace(query.Operation) ||
                               string.Equals(envelope.Operation, query.Operation, StringComparison.OrdinalIgnoreCase))
            .Where(envelope => string.IsNullOrWhiteSpace(query.CorrelationId) ||
                               string.Equals(envelope.CorrelationId, query.CorrelationId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(envelope => envelope.CreatedOnUtc)
            .Take(Math.Max(1, query.Limit))
            .ToArray();

        return ValueTask.FromResult<IReadOnlyList<OutboxEnvelope>>(result);
    }

    private OutboxEnvelope GetExisting(Ulid envelopeId)
        => _envelopes.TryGetValue(envelopeId, out var envelope)
            ? envelope
            : throw new KeyNotFoundException($"Outbox envelope '{envelopeId}' was not found.");
}
