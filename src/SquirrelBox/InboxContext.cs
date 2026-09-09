namespace SquirrelBox;

public sealed class InboxContext
{
    public InboxContext(InboxEntry entry, string owner, bool ownsCompletion, InboxContext previous = null)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        Owner = string.IsNullOrWhiteSpace(owner) ? "manual" : owner;
        OwnsCompletion = ownsCompletion;
        Previous = previous;
    }

    public InboxEntry Entry { get; }

    public string Owner { get; }

    public bool OwnsCompletion { get; }

    public InboxContext Previous { get; }

    public string EffectiveIdempotencyKey => Entry.IdempotencyKey;

    public InboxIdempotencyKeySource IdempotencyKeySource => Entry.IdempotencyKeySource;
}
