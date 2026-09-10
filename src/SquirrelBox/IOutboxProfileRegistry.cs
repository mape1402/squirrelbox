namespace SquirrelBox;

/// <summary>
/// Resolves an outbox profile for enqueue requests.
/// </summary>
public interface IOutboxProfileRegistry
{
    /// <summary>
    /// Resolves the best profile for the specified request.
    /// </summary>
    /// <param name="request">The enqueue request.</param>
    /// <returns>The resolved profile.</returns>
    IOutboxProfile Resolve(OutboxEnqueueRequest request);
}
