namespace SquirrelBox;

public interface IInboxStore
{
    ValueTask<InboxBeginResult> TryBeginAsync(InboxEntry entry, CancellationToken cancellationToken = default);

    ValueTask MarkCompletedAsync(Guid entryId, DateTimeOffset completedOnUtc, CancellationToken cancellationToken = default);

    ValueTask MarkFailedAsync(Guid entryId, string failure, DateTimeOffset failedOnUtc, CancellationToken cancellationToken = default);
}
