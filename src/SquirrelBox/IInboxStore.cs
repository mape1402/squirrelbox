namespace SquirrelBox;

/// <summary>
/// Persists inbox entries and state transitions.
/// </summary>
public interface IInboxStore
{
    /// <summary>
    /// Attempts to insert a new inbox entry or returns the duplicate state for an existing entry.
    /// </summary>
    ValueTask<InboxOpenResult> TryOpenAsync(InboxEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attaches or verifies a payload hash for an existing inbox entry.
    /// </summary>
    ValueTask<InboxPayloadVerificationResult> AttachPayloadHashAsync(Ulid entryId, string payloadHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an inbox entry as completed.
    /// </summary>
    ValueTask MarkCompletedAsync(Ulid entryId, InboxCompletion completion, DateTimeOffset completedOnUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an inbox entry as failed.
    /// </summary>
    ValueTask MarkFailedAsync(Ulid entryId, InboxFailure failure, DateTimeOffset failedOnUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an inbox entry by id.
    /// </summary>
    ValueTask<InboxEntry> GetAsync(Ulid entryId, CancellationToken cancellationToken = default);
}
