using Mule;
using Pigeon.Messaging.Consuming.Dispatching;
using Pigeon.Messaging.Contracts;
using SquirrelBox.Mule;

namespace SquirrelBox.Messaging.Pigeon;

/// <summary>
/// Mule action that replays a deferred Pigeon consume envelope.
/// </summary>
[MuleAction(SquirrelBoxPigeonMuleActionKeys.Consume)]
public sealed class SquirrelBoxPigeonMuleAction : SquirrelBoxMuleAction<PigeonConsumeEnvelope>
{
    private readonly IPigeonConsumerInvoker _consumerInvoker;

    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxPigeonMuleAction"/> class.
    /// </summary>
    /// <param name="inbox">The inbox service.</param>
    /// <param name="consumerInvoker">The Pigeon consumer invoker.</param>
    public SquirrelBoxPigeonMuleAction(IInboxService inbox, IPigeonConsumerInvoker consumerInvoker)
        : base(inbox)
    {
        _consumerInvoker = consumerInvoker ?? throw new ArgumentNullException(nameof(consumerInvoker));
    }

    /// <inheritdoc />
    protected override async ValueTask ExecuteInboxAsync(
        SquirrelBoxMuleActionContext<PigeonConsumeEnvelope> context,
        CancellationToken cancellationToken)
    {
        await _consumerInvoker.InvokeAsync(NormalizeEnvelope(context.Payload), cancellationToken);
    }

    private static PigeonConsumeEnvelope NormalizeEnvelope(PigeonConsumeEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var version = ResolveVersion(envelope);
        if (!IsUnset(envelope.Version))
            return envelope;

        return new PigeonConsumeEnvelope
        {
            MessageId = envelope.MessageId,
            CorrelationId = envelope.CorrelationId,
            Topic = envelope.Topic,
            Operation = envelope.Operation,
            Version = version,
            Subscription = envelope.Subscription,
            PayloadType = envelope.PayloadType,
            Payload = envelope.Payload,
            ContentType = envelope.ContentType,
            CreatedOnUtc = envelope.CreatedOnUtc,
            From = envelope.From,
            Headers = envelope.Headers,
            Metadata = envelope.Metadata
        };
    }

    private static SemanticVersion ResolveVersion(PigeonConsumeEnvelope envelope)
    {
        if (envelope.Metadata is not null &&
            envelope.Metadata.TryGetValue(SquirrelBoxPigeonMetadataNames.Version, out var rawVersion) &&
            SemanticVersion.TryParse(rawVersion, out var version))
        {
            return version;
        }

        return SemanticVersion.Default;
    }

    private static bool IsUnset(SemanticVersion version)
        => version.Major == 0 && version.Minor == 0 && version.Patch == 0;
}
