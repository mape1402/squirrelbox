namespace SquirrelBox;

/// <summary>
/// Publishes outbox envelopes through a specific transport.
/// </summary>
public interface IOutboxTransportPublisher
{
    /// <summary>
    /// Gets the transport name handled by this publisher.
    /// </summary>
    string Transport { get; }

    /// <summary>
    /// Publishes an outbox envelope.
    /// </summary>
    /// <param name="envelope">The envelope to publish.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The publish result.</returns>
    ValueTask<OutboxPublishResult> PublishAsync(
        OutboxEnvelope envelope,
        CancellationToken cancellationToken = default);
}
