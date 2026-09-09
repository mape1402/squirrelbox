using Microsoft.Extensions.Options;

namespace SquirrelBox;

public sealed class DefaultInbox : IInboxService
{
    private readonly IInboxContextAccessor _contextAccessor;
    private readonly SquirrelBoxOptions _options;
    private readonly IInboxPayloadHasher _payloadHasher;
    private readonly IInboxStore _store;
    private readonly TimeProvider _timeProvider;
    private InboxContext _current;

    public DefaultInbox(
        IOptions<SquirrelBoxOptions> options,
        IInboxContextAccessor contextAccessor,
        IInboxPayloadHasher payloadHasher,
        IInboxStore store,
        TimeProvider timeProvider)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _payloadHasher = payloadHasher ?? throw new ArgumentNullException(nameof(payloadHasher));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public InboxContext Current => _current ?? _contextAccessor.Current;

    public async ValueTask<InboxOpenResult> OpenOrContinueAsync(InboxOpenRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _contextAccessor.Prepare();

        if (Current is { } current)
            return new InboxOpenResult(InboxOpenState.Continued, current);

        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Operation);

        var payloadHash = request.Payload is null ? null : _payloadHasher.ComputeHash(request.Payload);
        var idempotencyKey = request.IdempotencyKey;
        var keySource = InboxIdempotencyKeySource.Explicit;

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var allowPayloadFallback = request.AllowPayloadHashAsIdempotencyKey ?? _options.AllowPayloadHashAsIdempotencyKey;
            if (!allowPayloadFallback || string.IsNullOrWhiteSpace(payloadHash))
                return new InboxOpenResult(InboxOpenState.MissingIdempotencyKey);

            idempotencyKey = payloadHash;
            keySource = InboxIdempotencyKeySource.ComputedFromPayload;
        }

        var now = _timeProvider.GetUtcNow();
        var entry = new InboxEntry
        {
            Id = Ulid.NewUlid(),
            Source = request.Source,
            Operation = request.Operation,
            IdempotencyKey = idempotencyKey,
            IdempotencyKeySource = keySource,
            PayloadHash = payloadHash,
            PayloadType = request.PayloadType ?? request.Payload?.GetType().AssemblyQualifiedName,
            CorrelationId = request.CorrelationId,
            ExecutionMode = request.ExecutionMode ?? _options.DefaultExecutionMode,
            Status = InboxStatus.Started,
            CreatedOnUtc = now,
            UpdatedOnUtc = now,
            ExpiresOnUtc = request.ExpiresOnUtc ?? ResolveDefaultExpiration(now),
            Metadata = new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase)
        };

        var result = await _store.TryOpenAsync(entry, cancellationToken);
        if (result.State == InboxOpenState.Opened)
        {
            var context = new InboxContext(
                result.Entry,
                request.Owner ?? _options.DefaultOwner,
                request.OwnsCompletion,
                Current);

            SetCurrent(context);
            return new InboxOpenResult(InboxOpenState.Opened, context);
        }

        return result;
    }

    public async ValueTask<InboxPayloadVerificationResult> VerifyCurrentPayloadAsync(
        object payload,
        CancellationToken cancellationToken = default)
    {
        if (Current is not { } context)
            return new InboxPayloadVerificationResult(InboxPayloadVerificationState.NoCurrentContext);

        ArgumentNullException.ThrowIfNull(payload);

        var payloadHash = _payloadHasher.ComputeHash(payload);
        return await _store.AttachPayloadHashAsync(context.Entry.Id, payloadHash, cancellationToken);
    }

    public async ValueTask CompleteCurrentAsync(
        InboxCompletion completion = null,
        CancellationToken cancellationToken = default)
    {
        var context = GetRequiredContext();

        await _store.MarkCompletedAsync(
            context.Entry.Id,
            completion ?? InboxCompletion.Empty,
            _timeProvider.GetUtcNow(),
            cancellationToken);

        RestorePreviousContext(context);
    }

    public ValueTask FailCurrentAsync(Exception exception, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return FailCurrentAsync(InboxFailure.FromException(exception), cancellationToken);
    }

    public async ValueTask FailCurrentAsync(InboxFailure failure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var context = GetRequiredContext();

        await _store.MarkFailedAsync(context.Entry.Id, failure, _timeProvider.GetUtcNow(), cancellationToken);

        RestorePreviousContext(context);
    }

    public ValueTask<InboxEntry> GetAsync(Ulid entryId, CancellationToken cancellationToken = default)
        => _store.GetAsync(entryId, cancellationToken);

    private DateTimeOffset? ResolveDefaultExpiration(DateTimeOffset now)
        => _options.DefaultEntryLifetime is { } lifetime && lifetime > TimeSpan.Zero
            ? now.Add(lifetime)
            : null;

    private InboxContext GetRequiredContext()
        => Current ?? throw new InboxContextUnavailableException();

    private void RestorePreviousContext(InboxContext context)
    {
        if (ReferenceEquals(Current, context))
            SetCurrent(context.Previous);
    }

    private void SetCurrent(InboxContext context)
    {
        _current = context;
        _contextAccessor.Current = context;
    }
}
