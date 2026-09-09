using Microsoft.Extensions.Options;
using Pigeon.Messaging.Consuming.Dispatching;
using SquirrelBox.Messaging;

namespace SquirrelBox.Messaging.Pigeon;

/// <summary>
/// Pigeon consume interceptor that opens a SquirrelBox inbox context before a HubConsumer executes.
/// </summary>
public sealed class SquirrelBoxPigeonConsumeInterceptor : IConsumeInterceptor
{
    private readonly IInboxMessageService _messages;
    private readonly SquirrelBoxPigeonOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxPigeonConsumeInterceptor"/> class.
    /// </summary>
    /// <param name="messages">The transport-neutral messaging inbox service.</param>
    /// <param name="options">The Pigeon adapter options.</param>
    public SquirrelBoxPigeonConsumeInterceptor(
        IInboxMessageService messages,
        IOptions<SquirrelBoxPigeonOptions> options)
    {
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async ValueTask Intercept(ConsumeContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var metadata = context.RawMetadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(context.RawMetadata, StringComparer.OrdinalIgnoreCase);

        var messageContext = new InboxMessageContext
        {
            Transport = _options.Transport,
            Topic = context.Topic,
            Version = context.MessageVersion.ToString(),
            Subscription = context.Subscription,
            Operation = _options.OperationResolver?.Invoke(context) ?? context.MessageType?.Name ?? "message",
            MessageId = ResolveMessageId(metadata),
            CorrelationId = ResolveCorrelationId(metadata),
            Payload = context.Message,
            Metadata = metadata
        };

        var result = await _messages.OpenAsync(messageContext, cancellationToken);
        if (!result.ShouldExecute)
            throw new SquirrelBoxPigeonRejectedException(result.Decision);
    }

    private string ResolveMessageId(IReadOnlyDictionary<string, string> metadata)
        => !string.IsNullOrWhiteSpace(_options.MessageIdMetadataName) &&
           metadata.TryGetValue(_options.MessageIdMetadataName, out var value)
            ? value
            : null;

    private static string ResolveCorrelationId(IReadOnlyDictionary<string, string> metadata)
        => metadata.TryGetValue("correlation-id", out var value) ||
           metadata.TryGetValue("x-correlation-id", out value)
            ? value
            : null;
}
