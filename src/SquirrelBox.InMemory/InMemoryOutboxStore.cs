using System.Collections.Concurrent;

namespace SquirrelBox.InMemory;

/// <summary>
/// In-memory implementation of <see cref="IOutboxStore"/> for tests and local scenarios.
/// </summary>
public sealed class InMemoryOutboxStore : IOutboxStore, IOutboxAttemptDiagnosticsStore
{
    private readonly ConcurrentDictionary<Ulid, OutboxEnvelope> _envelopes = new();
    private readonly ConcurrentDictionary<Ulid, List<OutboxAttempt>> _attempts = new();

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
        StartAttempt(envelope, publishingOnUtc);
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
        CompleteAttempt(envelope, OutboxAttemptState.Published, publishedOnUtc);
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
        CompleteAttempt(envelope, OutboxAttemptState.Failed, failedOnUtc, failure);
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
        CompleteAttempt(envelope, OutboxAttemptState.Discarded, discardedOnUtc);
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

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<OutboxAttempt>> QueryAttemptsAsync(
        OutboxAttemptQuery query,
        CancellationToken cancellationToken = default)
    {
        query ??= new OutboxAttemptQuery();

        var result = _attempts.Values
            .SelectMany(attempts => attempts)
            .Where(attempt => query.OutboxEnvelopeId is null || attempt.OutboxEnvelopeId == query.OutboxEnvelopeId.Value)
            .Where(attempt => query.State is null || attempt.State == query.State.Value)
            .Where(attempt => string.IsNullOrWhiteSpace(query.TraceId) ||
                              string.Equals(attempt.TraceId, query.TraceId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(attempt => attempt.StartedOnUtc)
            .Take(Math.Max(1, query.Limit))
            .Select(CloneAttempt)
            .ToArray();

        return ValueTask.FromResult<IReadOnlyList<OutboxAttempt>>(result);
    }

    private OutboxEnvelope GetExisting(Ulid envelopeId)
        => _envelopes.TryGetValue(envelopeId, out var envelope)
            ? envelope
            : throw new KeyNotFoundException($"Outbox envelope '{envelopeId}' was not found.");

    private void StartAttempt(OutboxEnvelope envelope, DateTimeOffset startedOnUtc)
    {
        var id = Ulid.NewUlid();
        var attempt = new OutboxAttempt
        {
            Id = id,
            OutboxEnvelopeId = envelope.Id,
            AttemptId = id.ToString(),
            TraceId = envelope.TraceId,
            State = OutboxAttemptState.Publishing,
            StartedOnUtc = startedOnUtc
        };

        WriteMetadata(attempt, envelope);
        _attempts.AddOrUpdate(
            envelope.Id,
            _ => [attempt],
            (_, attempts) =>
            {
                lock (attempts)
                    attempts.Add(attempt);

                return attempts;
            });
    }

    private void CompleteAttempt(
        OutboxEnvelope envelope,
        OutboxAttemptState state,
        DateTimeOffset completedOnUtc,
        OutboxFailure failure = null)
    {
        var attempts = _attempts.GetOrAdd(envelope.Id, _ => []);
        lock (attempts)
        {
            var attempt = attempts
                .Where(candidate => candidate.CompletedOnUtc is null)
                .OrderByDescending(candidate => candidate.StartedOnUtc)
                .FirstOrDefault();

            if (attempt is null)
            {
                var id = Ulid.NewUlid();
                attempt = new OutboxAttempt
                {
                    Id = id,
                    OutboxEnvelopeId = envelope.Id,
                    AttemptId = id.ToString(),
                    TraceId = envelope.TraceId,
                    StartedOnUtc = completedOnUtc
                };
                WriteMetadata(attempt, envelope);
                attempts.Add(attempt);
            }

            attempt.State = state;
            attempt.CompletedOnUtc = completedOnUtc;
            attempt.Failure = failure;
        }
    }

    private static void WriteMetadata(OutboxAttempt attempt, OutboxEnvelope envelope)
    {
        attempt.Metadata["transport"] = envelope.Transport;
        attempt.Metadata["operation"] = envelope.Operation;
        attempt.Metadata["destination"] = envelope.Destination;
    }

    private static OutboxAttempt CloneAttempt(OutboxAttempt attempt)
        => new()
        {
            Id = attempt.Id,
            OutboxEnvelopeId = attempt.OutboxEnvelopeId,
            AttemptId = attempt.AttemptId,
            TraceId = attempt.TraceId,
            State = attempt.State,
            StartedOnUtc = attempt.StartedOnUtc,
            CompletedOnUtc = attempt.CompletedOnUtc,
            Failure = attempt.Failure,
            Metadata = new Dictionary<string, string>(attempt.Metadata, StringComparer.OrdinalIgnoreCase)
        };
}
