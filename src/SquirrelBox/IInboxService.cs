namespace SquirrelBox;

/// <summary>
/// Opens, continues, completes, and fails inbox contexts for transport-agnostic idempotency.
/// </summary>
public interface IInboxService
{
    /// <summary>
    /// Gets the current ambient inbox context for the active asynchronous flow.
    /// </summary>
    InboxContext Current { get; }

    /// <summary>
    /// Gets the last inbox context opened or continued by this scoped service.
    /// </summary>
    InboxContext LastContext { get; }

    /// <summary>
    /// Opens a new inbox entry or continues an existing current context.
    /// </summary>
    /// <param name="request">The open request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The open result.</returns>
    ValueTask<InboxOpenResult> OpenOrContinueAsync(InboxOpenRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the current inbox entry against a payload hash, attaching the hash when the entry was opened without one.
    /// </summary>
    /// <param name="payload">The payload to verify.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The verification result.</returns>
    ValueTask<InboxPayloadVerificationResult> VerifyCurrentPayloadAsync(object payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the current inbox context as completed and restores the previous context.
    /// </summary>
    /// <param name="completion">Optional completion metadata.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the entry is marked completed.</returns>
    ValueTask CompleteCurrentAsync(InboxCompletion completion = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the current inbox context as failed using exception details.
    /// </summary>
    /// <param name="exception">The exception that failed the current context.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the entry is marked failed.</returns>
    ValueTask FailCurrentAsync(Exception exception, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the current inbox context as failed using explicit failure details.
    /// </summary>
    /// <param name="failure">The failure details.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the entry is marked failed.</returns>
    ValueTask FailCurrentAsync(InboxFailure failure, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads an inbox entry by ULID.
    /// </summary>
    /// <param name="entryId">The entry id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The stored entry.</returns>
    ValueTask<InboxEntry> GetAsync(Ulid entryId, CancellationToken cancellationToken = default);
}
