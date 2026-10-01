namespace SquirrelBox.Messaging;

/// <summary>
/// Resolves messaging inbox policies for incoming transport messages.
/// </summary>
public interface IInboxMessagePolicyRegistry
{
    /// <summary>
    /// Attempts to resolve the inbox policy that applies to a message context.
    /// </summary>
    /// <param name="context">The incoming message context.</param>
    /// <param name="policy">The matching policy when one exists.</param>
    /// <returns><see langword="true"/> when a policy matched; otherwise, <see langword="false"/>.</returns>
    bool TryResolve(InboxMessageContext context, out InboxMessagePolicy policy);
}
