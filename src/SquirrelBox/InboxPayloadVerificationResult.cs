namespace SquirrelBox;

/// <summary>
/// Represents the result of verifying a payload for the current inbox context.
/// </summary>
public sealed class InboxPayloadVerificationResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InboxPayloadVerificationResult"/> class.
    /// </summary>
    public InboxPayloadVerificationResult(InboxPayloadVerificationState state, InboxEntry entry = null)
    {
        State = state;
        Entry = entry;
    }

    /// <summary>
    /// Gets the verification state.
    /// </summary>
    public InboxPayloadVerificationState State { get; }

    /// <summary>
    /// Gets the related inbox entry.
    /// </summary>
    public InboxEntry Entry { get; }

    /// <summary>
    /// Gets a value indicating whether verification succeeded.
    /// </summary>
    public bool Success => State is InboxPayloadVerificationState.Verified or InboxPayloadVerificationState.Attached;
}
