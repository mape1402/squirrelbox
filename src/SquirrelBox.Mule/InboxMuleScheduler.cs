using Mule;

namespace SquirrelBox.Mule;

/// <summary>
/// Default scheduler that enqueues Mule durable actions for the current inbox context.
/// </summary>
public sealed class InboxMuleScheduler : IInboxMuleScheduler
{
    private readonly IInboxService _inbox;
    private readonly IMuleClient _mule;

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxMuleScheduler"/> class.
    /// </summary>
    /// <param name="inbox">The current inbox service.</param>
    /// <param name="mule">The Mule client.</param>
    public InboxMuleScheduler(IInboxService inbox, IMuleClient mule)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _mule = mule ?? throw new ArgumentNullException(nameof(mule));
    }

    /// <inheritdoc />
    public ValueTask<Guid> EnqueueCurrentAsync<TPayload>(
        ActionKey key,
        TPayload payload,
        Action<EnqueueOptions> configure = null,
        CancellationToken cancellationToken = default)
    {
        var context = _inbox.Current ?? throw new InboxContextUnavailableException();

        return _mule.EnqueueAsync(key, payload, options =>
        {
            options.Metadata["squirrelbox-inbox-id"] = context.Entry.Id.ToString();
            options.Metadata["idempotency-key"] = context.EffectiveIdempotencyKey;
            configure?.Invoke(options);
        }, cancellationToken);
    }
}
