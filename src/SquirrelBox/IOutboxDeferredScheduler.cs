namespace SquirrelBox;

/// <summary>
/// Schedules durable execution for persisted outbox envelopes.
/// </summary>
public interface IOutboxDeferredScheduler
{
    /// <summary>
    /// Schedules publication for a persisted envelope.
    /// </summary>
    /// <param name="envelope">The persisted envelope.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the scheduler accepts the envelope.</returns>
    ValueTask ScheduleAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default);
}
