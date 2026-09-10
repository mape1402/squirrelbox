namespace SquirrelBox;

/// <summary>
/// Enqueues, publishes, and inspects transport-agnostic outbox envelopes.
/// </summary>
public interface IOutboxService
{
    /// <summary>
    /// Gets the current ambient outbox context.
    /// </summary>
    OutboxContext Current { get; }

    /// <summary>
    /// Serializes, persists, and schedules a new outbox envelope.
    /// </summary>
    /// <param name="request">The enqueue request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The persisted envelope.</returns>
    ValueTask<OutboxEnvelope> EnqueueAsync(
        OutboxEnqueueRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a persisted envelope by id.
    /// </summary>
    /// <param name="envelopeId">The envelope id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The publish result.</returns>
    ValueTask<OutboxPublishResult> PublishAsync(
        Ulid envelopeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an outbox envelope by id.
    /// </summary>
    /// <param name="envelopeId">The envelope id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The stored envelope.</returns>
    ValueTask<OutboxEnvelope> GetAsync(
        Ulid envelopeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Queries outbox envelopes for diagnostics.
    /// </summary>
    /// <param name="query">The diagnostics query.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching envelopes.</returns>
    ValueTask<IReadOnlyList<OutboxEnvelope>> QueryAsync(
        OutboxQuery query,
        CancellationToken cancellationToken = default);
}
