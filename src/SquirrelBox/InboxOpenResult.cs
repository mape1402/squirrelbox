namespace SquirrelBox;

public sealed class InboxOpenResult
{
    public InboxOpenResult(InboxOpenState state, InboxContext context = null, InboxEntry entry = null)
    {
        State = state;
        Context = context;
        Entry = entry ?? context?.Entry;
    }

    public InboxOpenState State { get; }

    public InboxContext Context { get; }

    public InboxEntry Entry { get; }

    public bool Accepted => State is InboxOpenState.Opened or InboxOpenState.Continued;

    public string EffectiveIdempotencyKey => Entry?.IdempotencyKey;

    public InboxIdempotencyKeySource? IdempotencyKeySource => Entry?.IdempotencyKeySource;
}
