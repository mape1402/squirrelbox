namespace SquirrelBox;

public interface IInboxStore
{
    ValueTask<InboxBeginResult> TryBeginAsync(InboxEntry entry, CancellationToken cancellationToken = default);

    ValueTask MarkCompletedAsync(Guid entryId, InboxCompletion completion, DateTimeOffset completedOnUtc, CancellationToken cancellationToken = default);

    ValueTask MarkFailedAsync(Guid entryId, InboxFailure failure, DateTimeOffset failedOnUtc, CancellationToken cancellationToken = default);

    ValueTask<InboxEntry> GetAsync(Guid entryId, CancellationToken cancellationToken = default);
}
