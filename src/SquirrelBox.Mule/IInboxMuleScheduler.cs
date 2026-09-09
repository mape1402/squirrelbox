using Mule;

namespace SquirrelBox.Mule;

/// <summary>
/// Schedules Mule durable actions from the current SquirrelBox inbox context.
/// </summary>
public interface IInboxMuleScheduler
{
    /// <summary>
    /// Enqueues a Mule durable action tied to the current inbox context.
    /// </summary>
    /// <typeparam name="TPayload">The Mule payload type.</typeparam>
    /// <param name="key">The Mule action key.</param>
    /// <param name="payload">The payload to persist for deferred execution.</param>
    /// <param name="configure">Optional Mule enqueue configuration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The Mule durable action id.</returns>
    ValueTask<Guid> EnqueueCurrentAsync<TPayload>(
        ActionKey key,
        TPayload payload,
        Action<EnqueueOptions> configure = null,
        CancellationToken cancellationToken = default);
}
