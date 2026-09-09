using Microsoft.Extensions.Options;

namespace SquirrelBox;

public sealed class DefaultInbox : IInbox
{
    private readonly SquirrelBoxOptions _options;
    private readonly IInboxPayloadHasher _payloadHasher;
    private readonly IInboxStore _store;
    private readonly TimeProvider _timeProvider;

    public DefaultInbox(
        IOptions<SquirrelBoxOptions> options,
        IInboxPayloadHasher payloadHasher,
        IInboxStore store,
        TimeProvider timeProvider)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _payloadHasher = payloadHasher ?? throw new ArgumentNullException(nameof(payloadHasher));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public ValueTask<InboxBeginResult> BeginAsync(InboxRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.IdempotencyKey);
        ArgumentNullException.ThrowIfNull(request.Payload);

        var now = _timeProvider.GetUtcNow();
        var entry = new InboxEntry
        {
            Id = Guid.NewGuid(),
            Source = request.Source,
            Operation = request.Operation,
            IdempotencyKey = request.IdempotencyKey,
            PayloadHash = _payloadHasher.ComputeHash(request.Payload),
            PayloadType = request.PayloadType ?? request.Payload.GetType().AssemblyQualifiedName,
            CorrelationId = request.CorrelationId,
            ExecutionMode = request.ExecutionMode ?? _options.DefaultExecutionMode,
            Status = InboxStatus.Started,
            CreatedOnUtc = now,
            UpdatedOnUtc = now,
            ExpiresOnUtc = request.ExpiresOnUtc ?? ResolveDefaultExpiration(now),
            Metadata = new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase)
        };

        return _store.TryBeginAsync(entry, cancellationToken);
    }

    public ValueTask CompleteAsync(Guid entryId, InboxCompletion completion = null, CancellationToken cancellationToken = default)
        => _store.MarkCompletedAsync(
            entryId,
            completion ?? InboxCompletion.Empty,
            _timeProvider.GetUtcNow(),
            cancellationToken);

    public ValueTask FailAsync(Guid entryId, Exception exception, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return FailAsync(entryId, InboxFailure.FromException(exception), cancellationToken);
    }

    public ValueTask FailAsync(Guid entryId, InboxFailure failure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return _store.MarkFailedAsync(entryId, failure, _timeProvider.GetUtcNow(), cancellationToken);
    }

    public ValueTask<InboxEntry> GetAsync(Guid entryId, CancellationToken cancellationToken = default)
        => _store.GetAsync(entryId, cancellationToken);

    private DateTimeOffset? ResolveDefaultExpiration(DateTimeOffset now)
        => _options.DefaultEntryLifetime is { } lifetime && lifetime > TimeSpan.Zero
            ? now.Add(lifetime)
            : null;
}
