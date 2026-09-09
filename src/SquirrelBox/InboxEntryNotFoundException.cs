namespace SquirrelBox;

public sealed class InboxEntryNotFoundException : InvalidOperationException
{
    public InboxEntryNotFoundException(Ulid entryId)
        : base($"Inbox entry '{entryId}' was not found.")
    {
        EntryId = entryId;
    }

    public Ulid EntryId { get; }
}
