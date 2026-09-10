using Mule;

namespace SquirrelBox.Mule;

/// <summary>
/// Mule action that publishes a persisted SquirrelBox outbox envelope.
/// </summary>
[MuleAction(SquirrelBoxOutboxMuleActionKeys.Publish)]
public sealed class SquirrelBoxOutboxMuleAction : IMuleAction<SquirrelBoxOutboxEnvelopeReference>
{
    private readonly IOutboxService _outbox;

    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxOutboxMuleAction"/> class.
    /// </summary>
    /// <param name="outbox">The outbox service.</param>
    public SquirrelBoxOutboxMuleAction(IOutboxService outbox)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
    }

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(
        MuleActionContext<SquirrelBoxOutboxEnvelopeReference> context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var envelopeId = ResolveEnvelopeId(context);
        await _outbox.PublishAsync(envelopeId, cancellationToken);
    }

    private static Ulid ResolveEnvelopeId(MuleActionContext<SquirrelBoxOutboxEnvelopeReference> context)
    {
        var rawEnvelopeId = context.Payload?.EnvelopeId;
        if (string.IsNullOrWhiteSpace(rawEnvelopeId) &&
            context.Metadata.TryGetValue(SquirrelBoxMuleMetadata.OutboxEnvelopeId, out var metadataEnvelopeId))
        {
            rawEnvelopeId = metadataEnvelopeId;
        }

        if (string.IsNullOrWhiteSpace(rawEnvelopeId))
            throw new InvalidOperationException("SquirrelBox outbox Mule action is missing the envelope id.");

        try
        {
            return Ulid.Parse(rawEnvelopeId);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                $"SquirrelBox outbox envelope id '{rawEnvelopeId}' is not a valid ULID.",
                exception);
        }
    }
}
