namespace SquirrelBox;

public interface IInbox
{
    ValueTask<InboxBeginResult> BeginAsync(InboxRequest request, CancellationToken cancellationToken = default);

    ValueTask CompleteAsync(Guid entryId, InboxCompletion completion = null, CancellationToken cancellationToken = default);

    ValueTask FailAsync(Guid entryId, Exception exception, CancellationToken cancellationToken = default);

    ValueTask FailAsync(Guid entryId, InboxFailure failure, CancellationToken cancellationToken = default);

    ValueTask<InboxEntry> GetAsync(Guid entryId, CancellationToken cancellationToken = default);
}
