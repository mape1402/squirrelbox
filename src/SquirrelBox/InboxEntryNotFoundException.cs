namespace SquirrelBox;

/// <summary>
/// Exception thrown when an inbox entry cannot be found by id.
/// </summary>
public sealed class InboxEntryNotFoundException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InboxEntryNotFoundException"/> class.
    /// </summary>
    public InboxEntryNotFoundException(Ulid entryId)
        : base($"Inbox entry '{entryId}' was not found.")
    {
        EntryId = entryId;
    }

    /// <summary>
    /// Gets the missing inbox entry id.
    /// </summary>
    public Ulid EntryId { get; }
}
