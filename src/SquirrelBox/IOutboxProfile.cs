namespace SquirrelBox;

/// <summary>
/// Customizes how outbox enqueue requests are converted into durable envelopes.
/// </summary>
public interface IOutboxProfile
{
    /// <summary>
    /// Returns whether this profile should handle the specified enqueue request.
    /// </summary>
    /// <param name="request">The enqueue request.</param>
    /// <returns><see langword="true"/> when the profile can build the envelope.</returns>
    bool CanHandle(OutboxEnqueueRequest request);

    /// <summary>
    /// Builds the durable envelope for an enqueue request.
    /// </summary>
    /// <param name="request">The enqueue request.</param>
    /// <param name="context">The envelope build context.</param>
    /// <returns>The durable outbox envelope.</returns>
    OutboxEnvelope CreateEnvelope(OutboxEnqueueRequest request, OutboxProfileContext context);
}
