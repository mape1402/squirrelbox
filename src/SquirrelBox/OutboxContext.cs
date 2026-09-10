namespace SquirrelBox;

/// <summary>
/// Represents the current outbox envelope being enqueued or published.
/// </summary>
public sealed class OutboxContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxContext"/> class.
    /// </summary>
    /// <param name="envelope">The current outbox envelope.</param>
    /// <param name="previous">The previous ambient outbox context.</param>
    public OutboxContext(OutboxEnvelope envelope, OutboxContext previous = null)
    {
        Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
        Previous = previous;
    }

    /// <summary>
    /// Gets the current outbox envelope.
    /// </summary>
    public OutboxEnvelope Envelope { get; }

    /// <summary>
    /// Gets the previous ambient outbox context.
    /// </summary>
    public OutboxContext Previous { get; }
}
