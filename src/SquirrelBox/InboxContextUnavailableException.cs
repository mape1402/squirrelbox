namespace SquirrelBox;

public sealed class InboxContextUnavailableException : InvalidOperationException
{
    public InboxContextUnavailableException()
        : base("No current inbox context is available.")
    {
    }
}
