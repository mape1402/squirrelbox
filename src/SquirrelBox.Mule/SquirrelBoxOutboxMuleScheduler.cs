using Mule;

namespace SquirrelBox.Mule;

/// <summary>
/// Schedules persisted outbox envelopes through Mule durable actions.
/// </summary>
public sealed class SquirrelBoxOutboxMuleScheduler : IOutboxDeferredScheduler
{
    private readonly IMuleClient _mule;

    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxOutboxMuleScheduler"/> class.
    /// </summary>
    /// <param name="mule">The Mule client.</param>
    public SquirrelBoxOutboxMuleScheduler(IMuleClient mule)
    {
        _mule = mule ?? throw new ArgumentNullException(nameof(mule));
    }

    /// <inheritdoc />
    public async ValueTask ScheduleAsync(
        OutboxEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        await _mule.EnqueueAsync(
            SquirrelBoxOutboxMuleActionKeys.PublishKey,
            new SquirrelBoxOutboxEnvelopeReference { EnvelopeId = envelope.Id.ToString() },
            options =>
            {
                options.CorrelationId ??= envelope.CorrelationId;
                options.DeduplicationKey ??= envelope.Id.ToString();
                options.Metadata[SquirrelBoxMuleMetadata.OutboxEnvelopeId] = envelope.Id.ToString();
                options.Metadata["transport"] = envelope.Transport;
                options.Metadata["operation"] = envelope.Operation;
                options.Metadata["destination"] = envelope.Destination;
            },
            cancellationToken);
    }
}
