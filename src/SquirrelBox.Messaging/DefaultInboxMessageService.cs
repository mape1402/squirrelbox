using Microsoft.Extensions.Options;

namespace SquirrelBox.Messaging;

/// <summary>
/// Default transport-neutral implementation of <see cref="IInboxMessageService"/>.
/// </summary>
public sealed class DefaultInboxMessageService : IInboxMessageService
{
    private readonly IInboxService _inbox;
    private readonly IInboxPolicyResolver _policyResolver;
    private readonly IInboxMessagePolicyRegistry _policyRegistry;
    private readonly ISquirrelBoxMessageMetadataEnricher _metadataEnricher;
    private readonly SquirrelBoxMessagingOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultInboxMessageService"/> class.
    /// </summary>
    /// <param name="inbox">The core inbox service.</param>
    /// <param name="policyResolver">The core policy resolver.</param>
    /// <param name="policyRegistry">The messaging inbox policy registry.</param>
    /// <param name="metadataEnricher">The SquirrelBox message metadata enricher.</param>
    /// <param name="options">The messaging options.</param>
    public DefaultInboxMessageService(
        IInboxService inbox,
        IInboxPolicyResolver policyResolver,
        IInboxMessagePolicyRegistry policyRegistry,
        ISquirrelBoxMessageMetadataEnricher metadataEnricher,
        IOptions<SquirrelBoxMessagingOptions> options)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _policyResolver = policyResolver ?? throw new ArgumentNullException(nameof(policyResolver));
        _policyRegistry = policyRegistry ?? throw new ArgumentNullException(nameof(policyRegistry));
        _metadataEnricher = metadataEnricher ?? throw new ArgumentNullException(nameof(metadataEnricher));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async ValueTask<InboxMessageOpenResult> OpenAsync(
        InboxMessageContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.Transport);

        if (!_policyRegistry.TryResolve(context, out var policy))
            return InboxMessageOpenResult.Disabled;

        var metadata = context.Metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(context.Metadata, StringComparer.OrdinalIgnoreCase);
        SquirrelBoxMessageMetadata.TryReadFrom(metadata, out var squirrelBoxMetadata);
        var idempotencyKey = ResolveIdempotencyKey(context, squirrelBoxMetadata);
        var correlationId = ResolveCorrelationId(context, squirrelBoxMetadata);
        var traceId = ResolveTraceId(context, squirrelBoxMetadata);

        var request = new InboxOpenRequest
        {
            Source = context.Transport,
            Operation = _options.OperationResolver(context),
            IdempotencyKey = idempotencyKey.Value,
            IdempotencyKeyName = idempotencyKey.Name,
            Payload = context.Payload,
            PayloadType = context.Payload?.GetType().AssemblyQualifiedName,
            CorrelationId = correlationId.Value,
            CorrelationIdName = correlationId.Name,
            TraceId = traceId.Value,
            TraceIdName = traceId.Name,
            AttemptIdName = SquirrelBoxMetadataNames.MetadataSection,
            Owner = "messaging",
            ExecutionMode = context.ExecutionMode ?? policy.ExecutionMode ?? _options.ExecutionModeResolver?.Invoke(context),
            PolicyName = policy.CorePolicyName,
            EntryLifetime = policy.EntryLifetime,
            CompletedLock = policy.CompletedLock,
            AllowPayloadHashAsIdempotencyKey = policy.AllowPayloadHashAsIdempotencyKey ?? _options.AllowPayloadHashAsIdempotencyKey,
            Metadata = metadata
        };

        if (!string.IsNullOrWhiteSpace(context.MessageId))
            request.Metadata["message-id"] = context.MessageId;

        if (!string.IsNullOrWhiteSpace(context.Topic))
            request.Metadata["topic"] = context.Topic;

        if (!string.IsNullOrWhiteSpace(context.Version))
            request.Metadata["version"] = context.Version;

        var open = await _inbox.OpenOrContinueAsync(request, cancellationToken);
        return new InboxMessageOpenResult(open, _policyResolver.Resolve(open), _metadataEnricher.Create(open.Identity));
    }

    /// <inheritdoc />
    public void AttachEffectiveKey(IDictionary<string, string> metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var identity = _inbox.LastContext?.Identity;
        if (identity is null)
            return;

        var messageMetadata = _metadataEnricher.Create(identity);
        messageMetadata.WriteTo(metadata);
    }

    private ResolvedMetadata ResolveIdempotencyKey(
        InboxMessageContext context,
        SquirrelBoxMessageMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata?.IdempotencyKey))
        {
            return new ResolvedMetadata(
                SquirrelBoxMetadataNames.MetadataSection,
                metadata.IdempotencyKey);
        }

        if (_options.UseMessageIdWhenKeyIsMissing && !string.IsNullOrWhiteSpace(context.MessageId))
        {
            var messageIdName = string.IsNullOrWhiteSpace(_options.MessageIdMetadataName)
                ? "message-id"
                : _options.MessageIdMetadataName;
            return new ResolvedMetadata(messageIdName, context.MessageId);
        }

        if (_options.UseCorrelationIdWhenKeyIsMissing && !string.IsNullOrWhiteSpace(context.CorrelationId))
        {
            return new ResolvedMetadata(
                SquirrelBoxMetadataNames.MetadataSection,
                context.CorrelationId);
        }

        return new ResolvedMetadata(
            SquirrelBoxMetadataNames.MetadataSection,
            null);
    }

    private ResolvedMetadata ResolveCorrelationId(
        InboxMessageContext context,
        SquirrelBoxMessageMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata?.CorrelationId))
        {
            return new ResolvedMetadata(
                SquirrelBoxMetadataNames.MetadataSection,
                metadata.CorrelationId);
        }

        return new ResolvedMetadata(
            SquirrelBoxMetadataNames.MetadataSection,
            context.CorrelationId);
    }

    private ResolvedMetadata ResolveTraceId(
        InboxMessageContext context,
        SquirrelBoxMessageMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata?.TraceId))
        {
            return new ResolvedMetadata(
                SquirrelBoxMetadataNames.MetadataSection,
                metadata.TraceId);
        }

        return new ResolvedMetadata(
            SquirrelBoxMetadataNames.MetadataSection,
            context.TraceId);
    }

    private sealed record ResolvedMetadata(string Name, string Value);
}
