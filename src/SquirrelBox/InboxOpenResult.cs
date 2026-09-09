namespace SquirrelBox;

/// <summary>
/// Represents the result of attempting to open or continue an inbox context.
/// </summary>
public sealed class InboxOpenResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InboxOpenResult"/> class.
    /// </summary>
    public InboxOpenResult(InboxOpenState state, InboxContext context = null, InboxEntry entry = null)
    {
        State = state;
        Context = context;
        Entry = entry ?? context?.Entry;
    }

    /// <summary>
    /// Gets the open state.
    /// </summary>
    public InboxOpenState State { get; }

    /// <summary>
    /// Gets the opened or continued context, when accepted.
    /// </summary>
    public InboxContext Context { get; }

    /// <summary>
    /// Gets the related inbox entry, when available.
    /// </summary>
    public InboxEntry Entry { get; }

    /// <summary>
    /// Gets a value indicating whether protected work may execute.
    /// </summary>
    public bool Accepted => State is InboxOpenState.Opened or InboxOpenState.Continued;

    /// <summary>
    /// Gets the effective idempotency key, when available.
    /// </summary>
    public string EffectiveIdempotencyKey => Entry?.IdempotencyKey;

    /// <summary>
    /// Gets how the effective idempotency key was obtained.
    /// </summary>
    public InboxIdempotencyKeySource? IdempotencyKeySource => Entry?.IdempotencyKeySource;
}
