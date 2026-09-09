namespace SquirrelBox;

public sealed class InboxEntryNotFoundException : InvalidOperationException
{
    public InboxEntryNotFoundException(Guid entryId)
        : base($"Inbox entry '{entryId}' was not found.")
    {
        EntryId = entryId;
    }

    public Guid EntryId { get; }
}
