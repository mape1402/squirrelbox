using Microsoft.Extensions.Options;

namespace SquirrelBox.Messaging;

/// <summary>
/// Default transport-neutral implementation of <see cref="IInboxMessageService"/>.
/// </summary>
public sealed class DefaultInboxMessageService : IInboxMessageService
{
    private readonly IInboxService _inbox;
    private readonly IInboxPolicyResolver _policyResolver;
    private readonly SquirrelBoxMessagingOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultInboxMessageService"/> class.
    /// </summary>
    /// <param name="inbox">The core inbox service.</param>
    /// <param name="policyResolver">The core policy resolver.</param>
    /// <param name="options">The messaging options.</param>
    public DefaultInboxMessageService(
        IInboxService inbox,
        IInboxPolicyResolver policyResolver,
        IOptions<SquirrelBoxMessagingOptions> options)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _policyResolver = policyResolver ?? throw new ArgumentNullException(nameof(policyResolver));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async ValueTask<InboxMessageOpenResult> OpenAsync(
        InboxMessageContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Transport);

        var request = new InboxOpenRequest
        {
            Source = context.Transport,
            Operation = _options.OperationResolver(context),
            IdempotencyKey = ResolveIdempotencyKey(context),
            Payload = context.Payload,
            PayloadType = context.Payload?.GetType().AssemblyQualifiedName,
            CorrelationId = context.CorrelationId,
            Owner = "messaging",
            AllowPayloadHashAsIdempotencyKey = _options.AllowPayloadHashAsIdempotencyKey,
            Metadata = new Dictionary<string, string>(context.Metadata, StringComparer.OrdinalIgnoreCase)
        };

        if (!string.IsNullOrWhiteSpace(context.MessageId))
            request.Metadata["message-id"] = context.MessageId;

        if (!string.IsNullOrWhiteSpace(context.Topic))
            request.Metadata["topic"] = context.Topic;

        if (!string.IsNullOrWhiteSpace(context.Version))
            request.Metadata["version"] = context.Version;

        var open = await _inbox.OpenOrContinueAsync(request, cancellationToken);
        return new InboxMessageOpenResult(open, _policyResolver.Resolve(open));
    }

    /// <inheritdoc />
    public void AttachEffectiveKey(IDictionary<string, string> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (_inbox.LastContext?.EffectiveIdempotencyKey is { Length: > 0 } key)
            metadata[_options.ReplyIdempotencyKeyMetadataName] = key;
    }

    private string ResolveIdempotencyKey(InboxMessageContext context)
    {
        foreach (var name in _options.IdempotencyKeyMetadataNames)
        {
            if (context.Metadata.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }

        if (_options.UseMessageIdWhenKeyIsMissing && !string.IsNullOrWhiteSpace(context.MessageId))
            return context.MessageId;

        if (_options.UseCorrelationIdWhenKeyIsMissing && !string.IsNullOrWhiteSpace(context.CorrelationId))
            return context.CorrelationId;

        return null;
    }
}
