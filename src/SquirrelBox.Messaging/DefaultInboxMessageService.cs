using Microsoft.Extensions.Options;

namespace SquirrelBox.Messaging;

/// <summary>
/// Default transport-neutral implementation of <see cref="IInboxMessageService"/>.
/// </summary>
public sealed class DefaultInboxMessageService : IInboxMessageService
{
    private readonly IInboxService _inbox;
    private readonly IInboxPolicyResolver _policyResolver;
    private readonly ISquirrelBoxMessageMetadataEnricher _metadataEnricher;
    private readonly SquirrelBoxMessagingOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultInboxMessageService"/> class.
    /// </summary>
    /// <param name="inbox">The core inbox service.</param>
    /// <param name="policyResolver">The core policy resolver.</param>
    /// <param name="metadataEnricher">The SquirrelBox message metadata enricher.</param>
    /// <param name="options">The messaging options.</param>
    public DefaultInboxMessageService(
        IInboxService inbox,
        IInboxPolicyResolver policyResolver,
        ISquirrelBoxMessageMetadataEnricher metadataEnricher,
        IOptions<SquirrelBoxMessagingOptions> options)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _policyResolver = policyResolver ?? throw new ArgumentNullException(nameof(policyResolver));
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

        var metadata = context.Metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(context.Metadata, StringComparer.OrdinalIgnoreCase);
        var idempotencyKey = ResolveIdempotencyKey(context, metadata);
        var correlationId = ResolveCorrelationId(context, metadata);
        var traceId = ResolveTraceId(context, metadata);

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
            AttemptIdName = ResolveDefaultName(_options.AttemptIdMetadataNames, SquirrelBoxMetadataNames.AttemptId),
            Owner = "messaging",
            ExecutionMode = context.ExecutionMode ?? _options.ExecutionModeResolver?.Invoke(context),
            AllowPayloadHashAsIdempotencyKey = _options.AllowPayloadHashAsIdempotencyKey,
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

        if (messageMetadata.IdempotencyKey is null &&
            _inbox.LastContext.EffectiveIdempotencyKey is { Length: > 0 } key)
        {
            metadata[_options.ReplyIdempotencyKeyMetadataName] = key;
        }
    }

    private ResolvedMetadata ResolveIdempotencyKey(
        InboxMessageContext context,
        IReadOnlyDictionary<string, string> metadata)
    {
        if (ResolveMetadata(metadata, _options.IdempotencyKeyMetadataNames, SquirrelBoxMetadataNames.IdempotencyKey) is { } explicitKey)
            return explicitKey;

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
                ResolveDefaultName(_options.CorrelationIdMetadataNames, SquirrelBoxMetadataNames.CorrelationId),
                context.CorrelationId);
        }

        return new ResolvedMetadata(
            ResolveDefaultName(_options.IdempotencyKeyMetadataNames, SquirrelBoxMetadataNames.IdempotencyKey),
            null);
    }

    private ResolvedMetadata ResolveCorrelationId(
        InboxMessageContext context,
        IReadOnlyDictionary<string, string> metadata)
    {
        if (ResolveMetadata(metadata, _options.CorrelationIdMetadataNames, SquirrelBoxMetadataNames.CorrelationId) is { } correlationId)
            return correlationId;

        return new ResolvedMetadata(
            ResolveDefaultName(_options.CorrelationIdMetadataNames, SquirrelBoxMetadataNames.CorrelationId),
            context.CorrelationId);
    }

    private ResolvedMetadata ResolveTraceId(
        InboxMessageContext context,
        IReadOnlyDictionary<string, string> metadata)
    {
        if (ResolveMetadata(metadata, _options.TraceIdMetadataNames, SquirrelBoxMetadataNames.TraceId) is { } traceId)
            return traceId;

        return new ResolvedMetadata(
            ResolveDefaultName(_options.TraceIdMetadataNames, SquirrelBoxMetadataNames.TraceId),
            context.TraceId);
    }

    private static ResolvedMetadata ResolveMetadata(
        IReadOnlyDictionary<string, string> metadata,
        IEnumerable<string> names,
        string fallbackName)
    {
        foreach (var name in names)
        {
            if (!string.IsNullOrWhiteSpace(name) &&
                metadata.TryGetValue(name, out var value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return new ResolvedMetadata(name, value);
            }
        }

        return null;
    }

    private static string ResolveDefaultName(IEnumerable<string> names, string fallbackName)
    {
        foreach (var name in names)
        {
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        return fallbackName;
    }

    private sealed record ResolvedMetadata(string Name, string Value);
}
