namespace SquirrelBox;

/// <summary>
/// Provides access to the ambient inbox context for the current asynchronous flow.
/// </summary>
public interface IInboxContextAccessor
{
    /// <summary>
    /// Gets or sets the current ambient inbox context.
    /// </summary>
    InboxContext Current { get; set; }

    /// <summary>
    /// Prepares the current flow before opening or continuing a context.
    /// </summary>
    void Prepare();
}
