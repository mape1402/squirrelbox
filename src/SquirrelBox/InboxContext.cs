namespace SquirrelBox;

/// <summary>
/// Represents the inbox entry currently protecting a request, message, or pipeline operation.
/// </summary>
public sealed class InboxContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InboxContext"/> class.
    /// </summary>
    public InboxContext(InboxEntry entry, string owner, bool ownsCompletion, InboxContext previous = null)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        Owner = string.IsNullOrWhiteSpace(owner) ? "manual" : owner;
        OwnsCompletion = ownsCompletion;
        Previous = previous;
    }

    /// <summary>
    /// Gets the underlying inbox entry.
    /// </summary>
    public InboxEntry Entry { get; }

    /// <summary>
    /// Gets the component that opened the context.
    /// </summary>
    public string Owner { get; }

    /// <summary>
    /// Gets a value indicating whether this context owner should complete or fail the entry.
    /// </summary>
    public bool OwnsCompletion { get; }

    /// <summary>
    /// Gets the previous ambient inbox context, when nested.
    /// </summary>
    public InboxContext Previous { get; }

    /// <summary>
    /// Gets the effective idempotency key.
    /// </summary>
    public string EffectiveIdempotencyKey => Entry.IdempotencyKey;

    /// <summary>
    /// Gets how the effective idempotency key was obtained.
    /// </summary>
    public InboxIdempotencyKeySource IdempotencyKeySource => Entry.IdempotencyKeySource;
}
