using Microsoft.Extensions.Options;

namespace SquirrelBox;

/// <summary>
/// Default implementation of <see cref="IInboxService"/>.
/// </summary>
public sealed class DefaultInbox : IInboxService
{
    private readonly IInboxContextAccessor _contextAccessor;
    private readonly SquirrelBoxOptions _options;
    private readonly IInboxPayloadHasher _payloadHasher;
    private readonly IInboxStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly IInboxTransactionRunner _transactionRunner;
    private InboxContext _current;
    private InboxContext _lastContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultInbox"/> class.
    /// </summary>
    public DefaultInbox(
        IOptions<SquirrelBoxOptions> options,
        IInboxContextAccessor contextAccessor,
        IInboxPayloadHasher payloadHasher,
        IInboxStore store,
        TimeProvider timeProvider,
        IInboxTransactionRunner transactionRunner)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _payloadHasher = payloadHasher ?? throw new ArgumentNullException(nameof(payloadHasher));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _transactionRunner = transactionRunner ?? throw new ArgumentNullException(nameof(transactionRunner));
    }

    /// <inheritdoc />
    public InboxContext Current => _current ?? _contextAccessor.Current;

    /// <inheritdoc />
    public InboxContext LastContext => _lastContext;

    /// <inheritdoc />
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

        var result = await _transactionRunner.RunAsync(token => _store.TryOpenAsync(entry, token), cancellationToken);
        if (result.State == InboxOpenState.Opened)
        {
            var context = new InboxContext(
                result.Entry,
                request.Owner ?? _options.DefaultOwner,
                request.OwnsCompletion,
                Current);

            SetCurrent(context);
            _lastContext = context;
            return new InboxOpenResult(InboxOpenState.Opened, context);
        }

        if (result.Entry is not null)
            _lastContext = new InboxContext(result.Entry, request.Owner ?? _options.DefaultOwner, false, Current);

        return result;
    }

    /// <inheritdoc />
    public async ValueTask<InboxContext> ContinueAsync(
        Ulid entryId,
        string owner = null,
        bool ownsCompletion = true,
        CancellationToken cancellationToken = default)
    {
        _contextAccessor.Prepare();
        var resolvedOwner = owner ?? _options.DefaultOwner;

        if (Current is { } current &&
            current.Entry.Id == entryId &&
            string.Equals(current.Owner, resolvedOwner, StringComparison.Ordinal) &&
            current.OwnsCompletion == ownsCompletion)
        {
            return current;
        }

        var entry = await _transactionRunner.RunAsync(
            token => _store.GetAsync(entryId, token),
            cancellationToken);

        var context = new InboxContext(
            entry,
            resolvedOwner,
            ownsCompletion,
            Current);

        SetCurrent(context);
        _lastContext = context;
        return context;
    }

    /// <inheritdoc />
    public async ValueTask<InboxPayloadVerificationResult> VerifyCurrentPayloadAsync(
        object payload,
        CancellationToken cancellationToken = default)
    {
        if (Current is not { } context)
            return new InboxPayloadVerificationResult(InboxPayloadVerificationState.NoCurrentContext);

        ArgumentNullException.ThrowIfNull(payload);

        var payloadHash = _payloadHasher.ComputeHash(payload);
        return await _transactionRunner.RunAsync(
            token => _store.AttachPayloadHashAsync(context.Entry.Id, payloadHash, token),
            cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask CompleteCurrentAsync(
        InboxCompletion completion = null,
        CancellationToken cancellationToken = default)
    {
        var context = GetRequiredContext();

        await _transactionRunner.RunAsync(
            token => _store.MarkCompletedAsync(
                context.Entry.Id,
                completion ?? InboxCompletion.Empty,
                _timeProvider.GetUtcNow(),
                token),
            cancellationToken);

        RestorePreviousContext(context);
    }

    /// <inheritdoc />
    public ValueTask FailCurrentAsync(Exception exception, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return FailCurrentAsync(InboxFailure.FromException(exception), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask FailCurrentAsync(InboxFailure failure, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var context = GetRequiredContext();

        await _transactionRunner.RunAsync(
            token => _store.MarkFailedAsync(context.Entry.Id, failure, _timeProvider.GetUtcNow(), token),
            cancellationToken);

        RestorePreviousContext(context);
    }

    /// <inheritdoc />
    public ValueTask<InboxEntry> GetAsync(Ulid entryId, CancellationToken cancellationToken = default)
        => _transactionRunner.RunAsync(token => _store.GetAsync(entryId, token), cancellationToken);

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
