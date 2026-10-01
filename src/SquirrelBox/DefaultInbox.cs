using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace SquirrelBox;

/// <summary>
/// Default implementation of <see cref="IInboxService"/>.
/// </summary>
public sealed class DefaultInbox : IInboxService
{
    private readonly IInboxContextAccessor _contextAccessor;
    private readonly ISquirrelBoxIdentityAccessor _identityAccessor;
    private readonly SquirrelBoxOptions _options;
    private readonly IInboxPayloadHasher _payloadHasher;
    private readonly ICorrelationIdFactory _correlationIdFactory;
    private readonly ITraceIdFactory _traceIdFactory;
    private readonly IAttemptIdFactory _attemptIdFactory;
    private readonly ISquirrelBoxEventPublisher _events;
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
        ISquirrelBoxIdentityAccessor identityAccessor,
        IInboxPayloadHasher payloadHasher,
        ICorrelationIdFactory correlationIdFactory,
        ITraceIdFactory traceIdFactory,
        IAttemptIdFactory attemptIdFactory,
        ISquirrelBoxEventPublisher events,
        IInboxStore store,
        TimeProvider timeProvider,
        IInboxTransactionRunner transactionRunner)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _contextAccessor = contextAccessor ?? throw new ArgumentNullException(nameof(contextAccessor));
        _identityAccessor = identityAccessor ?? throw new ArgumentNullException(nameof(identityAccessor));
        _payloadHasher = payloadHasher ?? throw new ArgumentNullException(nameof(payloadHasher));
        _correlationIdFactory = correlationIdFactory ?? throw new ArgumentNullException(nameof(correlationIdFactory));
        _traceIdFactory = traceIdFactory ?? throw new ArgumentNullException(nameof(traceIdFactory));
        _attemptIdFactory = attemptIdFactory ?? throw new ArgumentNullException(nameof(attemptIdFactory));
        _events = events ?? throw new ArgumentNullException(nameof(events));
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
        _identityAccessor.Prepare();

        if (Current is { } current)
            return new InboxOpenResult(InboxOpenState.Continued, current);

        ArgumentException.ThrowIfNullOrWhiteSpace(request.Source);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Operation);

        var requestMetadata = request.Metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(request.Metadata, StringComparer.OrdinalIgnoreCase);
        var policy = ResolvePolicy(request.PolicyName);
        var payloadHash = request.Payload is null ? null : _payloadHasher.ComputeHash(request.Payload);
        var idempotencyKey = request.IdempotencyKey;
        var idempotencyKeyName = ResolveName(request.IdempotencyKeyName, SquirrelBoxMetadataNames.IdempotencyKey);
        var keySource = InboxIdempotencyKeySource.Explicit;

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var allowPayloadFallback = request.AllowPayloadHashAsIdempotencyKey ??
                                       policy.AllowPayloadHashAsIdempotencyKey ??
                                       _options.AllowPayloadHashAsIdempotencyKey;
            if (!allowPayloadFallback || string.IsNullOrWhiteSpace(payloadHash))
                return new InboxOpenResult(InboxOpenState.MissingIdempotencyKey);

            idempotencyKey = payloadHash;
            keySource = InboxIdempotencyKeySource.ComputedFromPayload;
        }

        var now = _timeProvider.GetUtcNow();
        var identityFactoryContext = new InboxIdentityFactoryContext
        {
            Source = request.Source,
            Operation = request.Operation,
            IdempotencyKey = idempotencyKey,
            IdempotencyKeySource = keySource,
            IncomingCorrelationId = request.CorrelationId,
            IncomingTraceId = request.TraceId,
            Payload = request.Payload,
            Metadata = requestMetadata
        };
        var correlationId = _correlationIdFactory.Create(identityFactoryContext);
        var traceId = _traceIdFactory.Create(identityFactoryContext);
        var attempt = new InboxAttempt
        {
            Id = Ulid.NewUlid(),
            AttemptId = _attemptIdFactory.Create(identityFactoryContext),
            AttemptIdName = ResolveName(request.AttemptIdName, SquirrelBoxMetadataNames.AttemptId),
            AttemptIdSource = SquirrelBoxMetadataValueSource.Generated,
            TraceId = traceId,
            TraceIdName = ResolveName(request.TraceIdName, SquirrelBoxMetadataNames.TraceId),
            TraceIdSource = ResolveTraceSource(request),
            CreatedOnUtc = now,
            Metadata = new Dictionary<string, string>(requestMetadata, StringComparer.OrdinalIgnoreCase)
        };
        var completedLock = ResolveCompletedLock(request, policy);
        var entry = new InboxEntry
        {
            Id = Ulid.NewUlid(),
            Source = request.Source,
            Operation = request.Operation,
            IdempotencyKey = idempotencyKey,
            IdempotencyKeyName = idempotencyKeyName,
            IdempotencyKeySource = keySource,
            PayloadHash = payloadHash,
            PayloadType = request.PayloadType ?? request.Payload?.GetType().AssemblyQualifiedName,
            CorrelationId = correlationId,
            CorrelationIdName = ResolveName(request.CorrelationIdName, SquirrelBoxMetadataNames.CorrelationId),
            CorrelationIdSource = string.IsNullOrWhiteSpace(request.CorrelationId)
                ? SquirrelBoxMetadataValueSource.Generated
                : SquirrelBoxMetadataValueSource.Incoming,
            OriginalAttemptId = attempt.AttemptId,
            OriginalTraceId = attempt.TraceId,
            LastAttemptId = attempt.AttemptId,
            LastTraceId = attempt.TraceId,
            CurrentAttempt = attempt,
            ExecutionMode = request.ExecutionMode ?? policy.ExecutionMode ?? _options.DefaultExecutionMode,
            PolicyName = policy.Name,
            CompletedLock = completedLock,
            Status = InboxStatus.Started,
            CreatedOnUtc = now,
            UpdatedOnUtc = now,
            ExpiresOnUtc = request.ExpiresOnUtc ?? ResolveExpiration(now, request, policy),
            Metadata = new Dictionary<string, string>(requestMetadata, StringComparer.OrdinalIgnoreCase)
        };
        attempt.InboxEntryId = entry.Id;

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
            _identityAccessor.Current = context.Identity;
            await PublishEventAsync(SquirrelBoxEventNames.InboxOpened, result.Entry, cancellationToken);
            await PublishEventAsync(SquirrelBoxEventNames.InboxAccepted, result.Entry, cancellationToken);
            return new InboxOpenResult(InboxOpenState.Opened, context);
        }

        if (result.Entry is not null)
        {
            _lastContext = new InboxContext(result.Entry, request.Owner ?? _options.DefaultOwner, false, Current);
            _identityAccessor.Current = _lastContext.Identity;
            await PublishEventAsync(SquirrelBoxEventNames.InboxDuplicated, result.Entry, cancellationToken);
        }

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
        _identityAccessor.Prepare();
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
        _identityAccessor.Current = context.Identity;
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
    public void ReleaseCurrent()
    {
        if (Current is { } context)
            RestorePreviousContext(context);
    }

    /// <inheritdoc />
    public async ValueTask CompleteCurrentAsync(
        InboxCompletion completion = null,
        CancellationToken cancellationToken = default)
    {
        var context = GetRequiredContext();
        var completedOnUtc = _timeProvider.GetUtcNow();
        var resolvedCompletion = completion ?? InboxCompletion.Empty;

        await _transactionRunner.RunAsync(
            token => _store.MarkCompletedAsync(
                context.Entry.Id,
                resolvedCompletion,
                completedOnUtc,
                token),
            cancellationToken);

        context.Entry.Status = InboxStatus.Completed;
        context.Entry.Completion = resolvedCompletion;
        context.Entry.CompletedOnUtc = completedOnUtc;
        context.Entry.UpdatedOnUtc = completedOnUtc;
        await PublishEventAsync(SquirrelBoxEventNames.InboxCompleted, context.Entry, cancellationToken);
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
        var failedOnUtc = _timeProvider.GetUtcNow();

        await _transactionRunner.RunAsync(
            token => _store.MarkFailedAsync(context.Entry.Id, failure, failedOnUtc, token),
            cancellationToken);

        context.Entry.Status = InboxStatus.Failed;
        context.Entry.Failure = failure.Details;
        context.Entry.FailureDetails = failure;
        context.Entry.UpdatedOnUtc = failedOnUtc;
        await PublishEventAsync(SquirrelBoxEventNames.InboxFailed, context.Entry, cancellationToken);
        RestorePreviousContext(context);
    }

    /// <inheritdoc />
    public ValueTask<InboxEntry> GetAsync(Ulid entryId, CancellationToken cancellationToken = default)
        => _transactionRunner.RunAsync(token => _store.GetAsync(entryId, token), cancellationToken);

    private InboxIdempotencyPolicy ResolvePolicy(string policyName)
    {
        if (!string.IsNullOrWhiteSpace(policyName) &&
            _options.InboxPolicies.TryGetValue(policyName, out var namedPolicy))
        {
            return namedPolicy;
        }

        return _options.DefaultInboxPolicy;
    }

    private DateTimeOffset? ResolveExpiration(
        DateTimeOffset now,
        InboxOpenRequest request,
        InboxIdempotencyPolicy policy)
    {
        var lifetime = request.EntryLifetime ??
                       policy.EntryLifetime ??
                       _options.DefaultEntryLifetime;

        return lifetime is { } value && value > TimeSpan.Zero
            ? now.Add(value)
            : null;
    }

    private static InboxCompletedLockMode ResolveCompletedLock(
        InboxOpenRequest request,
        InboxIdempotencyPolicy policy)
    {
        if (request.CompletedLock is not InboxCompletedLockMode.Default)
            return request.CompletedLock;

        return policy.CompletedLock is InboxCompletedLockMode.Default
            ? InboxCompletedLockMode.UntilExpiration
            : policy.CompletedLock;
    }

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
        _identityAccessor.Current = context?.Identity;
    }

    private static string ResolveName(string name, string fallback)
        => string.IsNullOrWhiteSpace(name) ? fallback : name;

    private static SquirrelBoxMetadataValueSource ResolveTraceSource(InboxOpenRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.TraceId))
            return SquirrelBoxMetadataValueSource.Incoming;

        return Activity.Current is null
            ? SquirrelBoxMetadataValueSource.Generated
            : SquirrelBoxMetadataValueSource.Activity;
    }

    private ValueTask PublishEventAsync(
        string name,
        InboxEntry entry,
        CancellationToken cancellationToken)
    {
        var @event = new SquirrelBoxEvent
        {
            Name = name,
            Category = "Inbox",
            SubjectId = entry.Id.ToString(),
            CorrelationId = entry.CorrelationId,
            Status = entry.Status.ToString(),
            OccurredOnUtc = _timeProvider.GetUtcNow()
        };

        @event.Metadata["source"] = entry.Source;
        @event.Metadata["operation"] = entry.Operation;
        @event.Metadata["idempotency-key"] = entry.IdempotencyKey;
        @event.Metadata["correlation-id"] = entry.CorrelationId;
        @event.Metadata["attempt-id"] = entry.CurrentAttempt?.AttemptId ?? entry.LastAttemptId;
        @event.Metadata["trace-id"] = entry.CurrentAttempt?.TraceId ?? entry.LastTraceId;
        @event.Metadata["execution-mode"] = entry.ExecutionMode.ToString();
        return _events.PublishAsync(@event, cancellationToken);
    }
}
