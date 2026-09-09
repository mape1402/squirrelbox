namespace SquirrelBox;

public interface IInboxStore
{
    ValueTask<InboxOpenResult> TryOpenAsync(InboxEntry entry, CancellationToken cancellationToken = default);

    ValueTask<InboxPayloadVerificationResult> AttachPayloadHashAsync(Ulid entryId, string payloadHash, CancellationToken cancellationToken = default);

    ValueTask MarkCompletedAsync(Ulid entryId, InboxCompletion completion, DateTimeOffset completedOnUtc, CancellationToken cancellationToken = default);

    ValueTask MarkFailedAsync(Ulid entryId, InboxFailure failure, DateTimeOffset failedOnUtc, CancellationToken cancellationToken = default);

    ValueTask<InboxEntry> GetAsync(Ulid entryId, CancellationToken cancellationToken = default);
}
