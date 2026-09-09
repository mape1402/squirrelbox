namespace SquirrelBox;

public sealed class InboxPayloadVerificationResult
{
    public InboxPayloadVerificationResult(InboxPayloadVerificationState state, InboxEntry entry = null)
    {
        State = state;
        Entry = entry;
    }

    public InboxPayloadVerificationState State { get; }

    public InboxEntry Entry { get; }

    public bool Success => State is InboxPayloadVerificationState.Verified or InboxPayloadVerificationState.Attached;
}
