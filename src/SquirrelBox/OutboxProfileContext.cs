namespace SquirrelBox;

/// <summary>
/// Provides dependencies and defaults used while an outbox profile builds an envelope.
/// </summary>
public sealed class OutboxProfileContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxProfileContext"/> class.
    /// </summary>
    /// <param name="serializer">The serializer used to persist payloads.</param>
    /// <param name="options">The outbox options.</param>
    /// <param name="createdOnUtc">The current UTC timestamp.</param>
    public OutboxProfileContext(
        IOutboxEnvelopeSerializer serializer,
        OutboxOptions options,
        DateTimeOffset createdOnUtc)
        : this(serializer, new DefaultOutboxPayloadTypeNameResolver(), options, createdOnUtc)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxProfileContext"/> class.
    /// </summary>
    /// <param name="serializer">The serializer used to persist payloads.</param>
    /// <param name="payloadTypes">The payload type name resolver.</param>
    /// <param name="options">The outbox options.</param>
    /// <param name="createdOnUtc">The current UTC timestamp.</param>
    public OutboxProfileContext(
        IOutboxEnvelopeSerializer serializer,
        IOutboxPayloadTypeNameResolver payloadTypes,
        OutboxOptions options,
        DateTimeOffset createdOnUtc)
    {
        Serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        PayloadTypes = payloadTypes ?? throw new ArgumentNullException(nameof(payloadTypes));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        CreatedOnUtc = createdOnUtc;
    }

    /// <summary>
    /// Gets the serializer used to persist payloads.
    /// </summary>
    public IOutboxEnvelopeSerializer Serializer { get; }

    /// <summary>
    /// Gets the payload type name resolver.
    /// </summary>
    public IOutboxPayloadTypeNameResolver PayloadTypes { get; }

    /// <summary>
    /// Gets the outbox options.
    /// </summary>
    public OutboxOptions Options { get; }

    /// <summary>
    /// Gets the UTC timestamp assigned to new envelopes.
    /// </summary>
    public DateTimeOffset CreatedOnUtc { get; }
}
