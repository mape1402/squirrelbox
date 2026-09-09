namespace SquirrelBox;

public interface IInbox
{
    ValueTask<InboxBeginResult> BeginAsync(InboxRequest request, CancellationToken cancellationToken = default);

    ValueTask CompleteAsync(Guid entryId, CancellationToken cancellationToken = default);

    ValueTask FailAsync(Guid entryId, Exception exception, CancellationToken cancellationToken = default);
}
