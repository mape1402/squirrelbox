namespace SquirrelBox.Messaging;

/// <summary>
/// Opens inbox contexts for transport-neutral messaging consumers.
/// </summary>
public interface IInboxMessageService
{
    /// <summary>
    /// Opens or continues an inbox context for an incoming message.
    /// </summary>
    /// <param name="context">The incoming message context.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The messaging open result.</returns>
    ValueTask<InboxMessageOpenResult> OpenAsync(InboxMessageContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches the current effective idempotency key to outgoing metadata when one is available.
    /// </summary>
    /// <param name="metadata">The outgoing metadata dictionary.</param>
    void AttachEffectiveKey(IDictionary<string, string> metadata);
}
