using Microsoft.Extensions.Options;
using Pigeon.Messaging.Consuming.Dispatching;
using Pigeon.Messaging.Contracts;
using SquirrelBox.Messaging;
using SquirrelBox.Mule;

namespace SquirrelBox.Messaging.Pigeon;

/// <summary>
/// Pigeon consume decision interceptor that opens inbox entries and optionally defers consumer execution.
/// </summary>
public sealed class SquirrelBoxPigeonDecisionInterceptor : IConsumeDecisionInterceptor
{
    private readonly IInboxMessageService _messages;
    private readonly ISquirrelBoxMessageMetadataEnricher _metadataEnricher;
    private readonly IInboxMuleScheduler _scheduler;
    private readonly IInboxService _inbox;
    private readonly IPigeonConsumeEnvelopeFactory _envelopeFactory;
    private readonly SquirrelBoxPigeonOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxPigeonDecisionInterceptor"/> class.
    /// </summary>
    public SquirrelBoxPigeonDecisionInterceptor(
        IInboxMessageService messages,
        ISquirrelBoxMessageMetadataEnricher metadataEnricher,
        IInboxMuleScheduler scheduler,
        IInboxService inbox,
        IPigeonConsumeEnvelopeFactory envelopeFactory,
        IOptions<SquirrelBoxPigeonOptions> options)
    {
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _metadataEnricher = metadataEnricher ?? throw new ArgumentNullException(nameof(metadataEnricher));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _envelopeFactory = envelopeFactory ?? throw new ArgumentNullException(nameof(envelopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async ValueTask<PigeonConsumeDecisionResult> InterceptAsync(
        ConsumeContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.ExecutionSource == ConsumeExecutionSource.DeferredReplay)
        {
            var metadata = CreateCurrentMetadata();
            metadata.WriteTo(context.ReplyMetadata);
            metadata.WriteTo(context.ReplyHeaders);
            return PigeonConsumeDecisionResult.Continue;
        }

        var messageContext = CreateMessageContext(context, out var version);
        var open = await _messages.OpenAsync(messageContext, cancellationToken);
        if (!open.Enabled)
            return PigeonConsumeDecisionResult.Continue;

        AttachEffectiveMetadata(context, open.EffectiveMetadata);

        if (!open.ShouldExecute)
            return MapDecision(open.Decision);

        if (open.OpenResult.Entry?.ExecutionMode == InboxExecutionMode.Deferred)
        {
            var envelope = WithVersion(_envelopeFactory.Create(context), version);
            envelope.Metadata[SquirrelBoxPigeonMetadataNames.Version] = version.ToString();
            open.EffectiveMetadata.WriteTo(envelope.Metadata);

            try
            {
                await _scheduler.EnqueueCurrentAsync(
                    SquirrelBoxPigeonMuleActionKeys.ConsumeKey,
                    envelope,
                    cancellationToken: cancellationToken);
            }
            catch (Exception exception)
            {
                if (_inbox.Current is not null)
                    await _inbox.FailCurrentAsync(exception, cancellationToken);

                throw;
            }

            if (_inbox.Current is not null)
                _inbox.ReleaseCurrent();

            return new PigeonConsumeDecisionResult(
                PigeonConsumeDecision.Defer,
                "SquirrelBox scheduled the Pigeon consumer for deferred execution.")
            {
                Metadata = CreateDecisionMetadata(open.EffectiveMetadata)
            };
        }

        return PigeonConsumeDecisionResult.Continue;
    }

    private InboxMessageContext CreateMessageContext(ConsumeContext context, out SemanticVersion version)
    {
        version = ResolveVersion(context);

        var metadata = context.RawMetadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(context.RawMetadata, StringComparer.OrdinalIgnoreCase);

        return new InboxMessageContext
        {
            Transport = _options.Transport,
            Topic = context.Topic,
            Version = version.ToString(),
            Subscription = context.Subscription,
            Operation = _options.OperationResolver?.Invoke(context) ??
                        context.Operation ??
                        context.MessageType?.Name ??
                        "message",
            MessageId = ResolveMessageId(context, metadata),
            CorrelationId = context.CorrelationId,
            ExecutionMode = _options.ExecutionModeResolver?.Invoke(context),
            Payload = context.Message,
            Metadata = metadata
        };
    }

    private SemanticVersion ResolveVersion(ConsumeContext context)
    {
        if (_options.VersionResolver?.Invoke(context) is { } resolvedVersion)
            return resolvedVersion;

        return IsUnset(context.MessageVersion)
            ? SemanticVersion.Default
            : context.MessageVersion;
    }

    private static bool IsUnset(SemanticVersion version)
        => version.Major == 0 && version.Minor == 0 && version.Patch == 0;

    private static PigeonConsumeEnvelope WithVersion(PigeonConsumeEnvelope envelope, SemanticVersion version)
    {
        ArgumentNullException.ThrowIfNull(envelope);

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
            Headers = envelope.Headers is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(envelope.Headers, StringComparer.OrdinalIgnoreCase),
            Metadata = envelope.Metadata is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(envelope.Metadata, StringComparer.OrdinalIgnoreCase)
        };
    }

    private PigeonConsumeDecisionResult MapDecision(InboxDecision decision)
    {
        var pigeonDecision = decision.Action switch
        {
            InboxPolicyAction.Skip => _options.InProgressDecision,
            InboxPolicyAction.Replay => PigeonConsumeDecision.AckAndSkip,
            InboxPolicyAction.Retry or InboxPolicyAction.Reopen => PigeonConsumeDecision.Retry,
            InboxPolicyAction.Reject or InboxPolicyAction.Fail => _options.RejectedDecision,
            _ => _options.RejectedDecision
        };

        return new PigeonConsumeDecisionResult(
            pigeonDecision,
            $"SquirrelBox inbox decision '{decision.Action}' for state '{decision.State}'.")
        {
            Metadata = CreateDecisionMetadata(_metadataEnricher.Create(_inbox.LastContext?.Identity))
        };
    }

    private SquirrelBoxMessageMetadata CreateCurrentMetadata()
        => _metadataEnricher.Create(_inbox.Current?.Identity ?? _inbox.LastContext?.Identity);

    private string ResolveMessageId(ConsumeContext context, IReadOnlyDictionary<string, string> metadata)
    {
        if (!string.IsNullOrWhiteSpace(context.MessageId))
            return context.MessageId;

        return !string.IsNullOrWhiteSpace(_options.MessageIdMetadataName) &&
               metadata.TryGetValue(_options.MessageIdMetadataName, out var value)
            ? value
            : null;
    }

    private static void AttachEffectiveMetadata(ConsumeContext context, SquirrelBoxMessageMetadata metadata)
    {
        metadata?.WriteTo(context.ReplyMetadata);
        metadata?.WriteTo(context.ReplyHeaders);
    }

    private static IReadOnlyDictionary<string, string> CreateDecisionMetadata(SquirrelBoxMessageMetadata metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        metadata?.WriteTo(result);

        return result;
    }
}
