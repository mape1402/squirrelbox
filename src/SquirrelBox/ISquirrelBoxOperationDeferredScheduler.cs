namespace SquirrelBox;

/// <summary>
/// Schedules declared SquirrelBox operations for deferred execution.
/// </summary>
public interface ISquirrelBoxOperationDeferredScheduler
{
    /// <summary>
    /// Schedules an operation envelope tied to the current inbox context.
    /// </summary>
    /// <param name="context">The current inbox context.</param>
    /// <param name="envelope">The durable operation envelope.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the envelope has been persisted.</returns>
    ValueTask ScheduleAsync(
        InboxContext context,
        SquirrelBoxOperationEnvelope envelope,
        CancellationToken cancellationToken = default);
}
