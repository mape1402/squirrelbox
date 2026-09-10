namespace SquirrelBox;

/// <summary>
/// Persists outbox envelopes and lifecycle transitions.
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    /// Adds a new outbox envelope.
    /// </summary>
    ValueTask AddAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an outbox envelope by id.
    /// </summary>
    ValueTask<OutboxEnvelope> GetAsync(Ulid envelopeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an outbox envelope as publishing.
    /// </summary>
    ValueTask MarkPublishingAsync(Ulid envelopeId, DateTimeOffset publishingOnUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an outbox envelope as published.
    /// </summary>
    ValueTask MarkPublishedAsync(Ulid envelopeId, DateTimeOffset publishedOnUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an outbox envelope as failed.
    /// </summary>
    ValueTask MarkFailedAsync(
        Ulid envelopeId,
        OutboxFailure failure,
        DateTimeOffset failedOnUtc,
        DateTimeOffset? nextAttemptOnUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an outbox envelope as discarded.
    /// </summary>
    ValueTask MarkDiscardedAsync(Ulid envelopeId, DateTimeOffset discardedOnUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries recent envelopes for diagnostics and dashboard history.
    /// </summary>
    ValueTask<IReadOnlyList<OutboxEnvelope>> QueryAsync(
        OutboxQuery query,
        CancellationToken cancellationToken = default);
}
