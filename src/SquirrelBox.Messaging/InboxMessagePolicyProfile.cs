namespace SquirrelBox.Messaging;

/// <summary>
/// Declares messaging inbox policies discovered from application assemblies.
/// </summary>
public abstract class InboxMessagePolicyProfile
{
    /// <summary>
    /// Configures messaging inbox policies.
    /// </summary>
    /// <param name="builder">The profile builder.</param>
    public abstract void Configure(InboxMessagePolicyProfileBuilder builder);
}
