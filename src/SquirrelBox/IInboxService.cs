namespace SquirrelBox;

public interface IInboxService
{
    InboxContext Current { get; }

    ValueTask<InboxOpenResult> OpenOrContinueAsync(InboxOpenRequest request, CancellationToken cancellationToken = default);

    ValueTask<InboxPayloadVerificationResult> VerifyCurrentPayloadAsync(object payload, CancellationToken cancellationToken = default);

    ValueTask CompleteCurrentAsync(InboxCompletion completion = null, CancellationToken cancellationToken = default);

    ValueTask FailCurrentAsync(Exception exception, CancellationToken cancellationToken = default);

    ValueTask FailCurrentAsync(InboxFailure failure, CancellationToken cancellationToken = default);

    ValueTask<InboxEntry> GetAsync(Ulid entryId, CancellationToken cancellationToken = default);
}
