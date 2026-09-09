namespace SquirrelBox;

public sealed class DefaultInbox : IInbox
{
    private readonly IInboxPayloadHasher _payloadHasher;
    private readonly IInboxStore _store;

    public DefaultInbox(IInboxPayloadHasher payloadHasher, IInboxStore store)
    {
        _payloadHasher = payloadHasher ?? throw new ArgumentNullException(nameof(payloadHasher));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public ValueTask<InboxBeginResult> BeginAsync(InboxRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        ArgumentNullException.ThrowIfNull(request.Payload);

        var now = DateTimeOffset.UtcNow;
        var entry = new InboxEntry
        {
            Id = Guid.NewGuid(),
            Source = request.Source,
            Operation = request.Operation,
            IdempotencyKey = request.IdempotencyKey,
            PayloadHash = _payloadHasher.ComputeHash(request.Payload),
            PayloadType = request.PayloadType ?? request.Payload.GetType().AssemblyQualifiedName,
            CorrelationId = request.CorrelationId,
            ExecutionMode = request.ExecutionMode,
            Status = InboxStatus.Started,
            CreatedOnUtc = now,
            UpdatedOnUtc = now,
            ExpiresOnUtc = request.ExpiresOnUtc
        };

        return _store.TryBeginAsync(entry, cancellationToken);
    }

    public ValueTask CompleteAsync(Guid entryId, CancellationToken cancellationToken = default)
        => _store.MarkCompletedAsync(entryId, DateTimeOffset.UtcNow, cancellationToken);

    public ValueTask FailAsync(Guid entryId, Exception exception, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return _store.MarkFailedAsync(entryId, exception.ToString(), DateTimeOffset.UtcNow, cancellationToken);
    }
}
