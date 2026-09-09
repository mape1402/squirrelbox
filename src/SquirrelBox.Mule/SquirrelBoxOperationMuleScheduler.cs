namespace SquirrelBox.Mule;

/// <summary>
/// Schedules declared SquirrelBox operations through Mule durable actions.
/// </summary>
public sealed class SquirrelBoxOperationMuleScheduler : ISquirrelBoxOperationDeferredScheduler
{
    private readonly IInboxMuleScheduler _scheduler;

    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxOperationMuleScheduler"/> class.
    /// </summary>
    /// <param name="scheduler">The current inbox Mule scheduler.</param>
    public SquirrelBoxOperationMuleScheduler(IInboxMuleScheduler scheduler)
    {
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
    }

    /// <inheritdoc />
    public async ValueTask ScheduleAsync(
        InboxContext context,
        SquirrelBoxOperationEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(envelope);

        await _scheduler.EnqueueCurrentAsync(
            SquirrelBoxOperationMuleActionKeys.OperationKey,
            envelope,
            cancellationToken: cancellationToken);
    }
}
