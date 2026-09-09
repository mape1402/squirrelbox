namespace SquirrelBox;

public sealed class InboxBeginResult
{
    public InboxBeginResult(InboxBeginState state, InboxEntry entry)
    {
        State = state;
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
    }

    public InboxBeginState State { get; }

    public InboxEntry Entry { get; }

    public bool Accepted => State == InboxBeginState.Started;
}
