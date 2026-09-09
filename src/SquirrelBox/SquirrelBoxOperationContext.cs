namespace SquirrelBox;

/// <summary>
/// Provides services and inbox state to a SquirrelBox operation.
/// </summary>
public sealed class SquirrelBoxOperationContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxOperationContext"/> class.
    /// </summary>
    public SquirrelBoxOperationContext(IServiceProvider services, IInboxService inbox, InboxContext inboxContext)
    {
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        InboxContext = inboxContext;
    }

    /// <summary>
    /// Gets the scoped service provider used to execute the operation.
    /// </summary>
    public IServiceProvider Services { get; }

    /// <summary>
    /// Gets the inbox service for the current execution.
    /// </summary>
    public IInboxService Inbox { get; }

    /// <summary>
    /// Gets the current inbox context when one is available.
    /// </summary>
    public InboxContext InboxContext { get; }
}
