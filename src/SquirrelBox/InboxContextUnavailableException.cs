namespace SquirrelBox;

/// <summary>
/// Exception thrown when an operation requires a current inbox context and none exists.
/// </summary>
public sealed class InboxContextUnavailableException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InboxContextUnavailableException"/> class.
    /// </summary>
    public InboxContextUnavailableException()
        : base("No current inbox context is available.")
    {
    }
}
