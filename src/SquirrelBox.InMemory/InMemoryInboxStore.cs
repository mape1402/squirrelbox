using System.Collections.Concurrent;

namespace SquirrelBox.InMemory;

/// <summary>
/// In-memory implementation of <see cref="IInboxStore"/> for tests and local scenarios.
/// </summary>
public sealed class InMemoryInboxStore : IInboxStore, IInboxDiagnosticsStore
{
    private readonly ConcurrentDictionary<string, InboxEntry> _entriesByKey = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Ulid, InboxEntry> _entriesById = new();
    private readonly ConcurrentDictionary<Ulid, ConcurrentQueue<InboxAttempt>> _attemptsByEntryId = new();

    /// <inheritdoc />
    public ValueTask<InboxOpenResult> TryOpenAsync(InboxEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var key = BuildKey(entry);
        var stored = _entriesByKey.GetOrAdd(key, _ =>
        {
            RecordAttempt(entry, InboxOpenState.Opened, entry);
            _entriesById[entry.Id] = entry;
            return entry;
        });

        if (ReferenceEquals(stored, entry))
            return ValueTask.FromResult(new InboxOpenResult(InboxOpenState.Opened, entry: stored));

        var isExpired = IsExpired(stored, entry.CreatedOnUtc);
        var completedForever = IsCompletedForever(stored);

        if ((!isExpired || completedForever) &&
            !string.IsNullOrWhiteSpace(stored.PayloadHash) &&
            !string.IsNullOrWhiteSpace(entry.PayloadHash) &&
            !string.Equals(stored.PayloadHash, entry.PayloadHash, StringComparison.Ordinal))
        {
            RecordAttempt(stored, InboxOpenState.PayloadConflict, entry);
            return ValueTask.FromResult(new InboxOpenResult(InboxOpenState.PayloadConflict, entry: stored));
        }

        if (ShouldReopen(stored, isExpired))
        {
            Reopen(stored, entry, preserveCorrelation: stored.Status is InboxStatus.Failed && !isExpired);
            RecordAttempt(stored, InboxOpenState.Opened, entry);
            return ValueTask.FromResult(new InboxOpenResult(InboxOpenState.Opened, entry: stored));
        }

        if (string.IsNullOrWhiteSpace(stored.PayloadHash) && !string.IsNullOrWhiteSpace(entry.PayloadHash))
            stored.PayloadHash = entry.PayloadHash;

        var state = MapDuplicateState(stored);
        RecordAttempt(stored, state, entry);
        return ValueTask.FromResult(new InboxOpenResult(state, entry: stored));
    }

    /// <inheritdoc />
    public ValueTask<InboxPayloadVerificationResult> AttachPayloadHashAsync(
        Ulid entryId,
        string payloadHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHash);

        var entry = GetExisting(entryId);

        if (string.IsNullOrWhiteSpace(entry.PayloadHash))
        {
            entry.PayloadHash = payloadHash;
            return ValueTask.FromResult(new InboxPayloadVerificationResult(InboxPayloadVerificationState.Attached, entry));
        }

        if (!string.Equals(entry.PayloadHash, payloadHash, StringComparison.Ordinal))
            return ValueTask.FromResult(new InboxPayloadVerificationResult(InboxPayloadVerificationState.PayloadConflict, entry));

        return ValueTask.FromResult(new InboxPayloadVerificationResult(InboxPayloadVerificationState.Verified, entry));
    }

    /// <inheritdoc />
    public ValueTask MarkCompletedAsync(
        Ulid entryId,
        InboxCompletion completion,
        DateTimeOffset completedOnUtc,
        CancellationToken cancellationToken = default)
    {
        var entry = GetExisting(entryId);

        entry.Status = InboxStatus.Completed;
        entry.Completion = completion;
        entry.CompletedOnUtc = completedOnUtc;
        entry.UpdatedOnUtc = completedOnUtc;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask MarkFailedAsync(
        Ulid entryId,
        InboxFailure failure,
        DateTimeOffset failedOnUtc,
        CancellationToken cancellationToken = default)
    {
        var entry = GetExisting(entryId);

        entry.Status = InboxStatus.Failed;
        entry.Failure = failure?.Details;
        entry.FailureDetails = failure;
        entry.UpdatedOnUtc = failedOnUtc;

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<InboxEntry> GetAsync(Ulid entryId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(GetExisting(entryId));

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<InboxEntry>> QueryAsync(
        InboxQuery query,
        CancellationToken cancellationToken = default)
    {
        query ??= new InboxQuery();

        var result = _entriesById.Values
            .Where(entry => query.Status is null || entry.Status == query.Status)
            .Where(entry => string.IsNullOrWhiteSpace(query.Source) ||
                            string.Equals(entry.Source, query.Source, StringComparison.OrdinalIgnoreCase))
            .Where(entry => string.IsNullOrWhiteSpace(query.Operation) ||
                            string.Equals(entry.Operation, query.Operation, StringComparison.OrdinalIgnoreCase))
            .Where(entry => string.IsNullOrWhiteSpace(query.IdempotencyKey) ||
                            string.Equals(entry.IdempotencyKey, query.IdempotencyKey, StringComparison.OrdinalIgnoreCase))
            .Where(entry => string.IsNullOrWhiteSpace(query.CorrelationId) ||
                            string.Equals(entry.CorrelationId, query.CorrelationId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(entry => entry.CreatedOnUtc)
            .Take(Math.Max(1, query.Limit))
            .ToArray();

        return ValueTask.FromResult<IReadOnlyList<InboxEntry>>(result);
    }

    private static string BuildKey(InboxEntry entry)
        => string.Join(":", entry.Source, entry.Operation, entry.IdempotencyKey);

    private static InboxOpenState MapDuplicateState(InboxEntry entry)
        => entry.Status switch
        {
            InboxStatus.Completed => InboxOpenState.DuplicateCompleted,
            InboxStatus.Failed => InboxOpenState.DuplicateFailed,
            InboxStatus.Expired => InboxOpenState.Expired,
            _ => InboxOpenState.DuplicateInProgress
        };

    private static bool IsExpired(InboxEntry entry, DateTimeOffset now)
        => entry.ExpiresOnUtc is { } expiresOnUtc && expiresOnUtc <= now;

    private static bool IsCompletedForever(InboxEntry entry)
        => entry.Status is InboxStatus.Completed &&
           entry.CompletedLock is InboxCompletedLockMode.Forever;

    private static bool ShouldReopen(InboxEntry entry, bool isExpired)
        => entry.Status is InboxStatus.Failed ||
           (isExpired && !IsCompletedForever(entry));

    private static void Reopen(
        InboxEntry stored,
        InboxEntry incoming,
        bool preserveCorrelation)
    {
        var correlationId = preserveCorrelation ? stored.CorrelationId : incoming.CorrelationId;
        var correlationIdName = preserveCorrelation ? stored.CorrelationIdName : incoming.CorrelationIdName;
        var correlationIdSource = preserveCorrelation ? stored.CorrelationIdSource : incoming.CorrelationIdSource;

        stored.IdempotencyKeyName = incoming.IdempotencyKeyName;
        stored.IdempotencyKeySource = incoming.IdempotencyKeySource;
        stored.PayloadHash = incoming.PayloadHash;
        stored.PayloadType = incoming.PayloadType;
        stored.CorrelationId = correlationId;
        stored.CorrelationIdName = correlationIdName;
        stored.CorrelationIdSource = correlationIdSource;
        stored.OriginalAttemptId = incoming.CurrentAttempt?.AttemptId;
        stored.OriginalTraceId = incoming.CurrentAttempt?.TraceId;
        stored.ExecutionMode = incoming.ExecutionMode;
        stored.PolicyName = incoming.PolicyName;
        stored.CompletedLock = incoming.CompletedLock is InboxCompletedLockMode.Default
            ? InboxCompletedLockMode.UntilExpiration
            : incoming.CompletedLock;
        stored.Status = InboxStatus.Started;
        stored.CreatedOnUtc = incoming.CreatedOnUtc;
        stored.UpdatedOnUtc = incoming.UpdatedOnUtc;
        stored.CompletedOnUtc = null;
        stored.ExpiresOnUtc = incoming.ExpiresOnUtc;
        stored.Failure = null;
        stored.FailureDetails = null;
        stored.Completion = null;
        stored.Metadata = new Dictionary<string, string>(incoming.Metadata, StringComparer.OrdinalIgnoreCase);
    }

    private void RecordAttempt(InboxEntry stored, InboxOpenState state, InboxEntry incoming)
    {
        if (incoming.CurrentAttempt is null)
            return;

        var attempt = CloneAttempt(incoming.CurrentAttempt, stored.Id, state);
        stored.CurrentAttempt = attempt;
        stored.LastAttemptId = attempt.AttemptId;
        stored.LastTraceId = attempt.TraceId;
        stored.Metadata[SquirrelBoxMetadataNames.AttemptId] = attempt.AttemptId;
        stored.Metadata[SquirrelBoxMetadataNames.TraceId] = attempt.TraceId;

        _attemptsByEntryId
            .GetOrAdd(stored.Id, _ => new ConcurrentQueue<InboxAttempt>())
            .Enqueue(attempt);
    }

    private static InboxAttempt CloneAttempt(InboxAttempt attempt, Ulid entryId, InboxOpenState state)
        => new()
        {
            Id = attempt.Id == default ? Ulid.NewUlid() : attempt.Id,
            InboxEntryId = entryId,
            AttemptId = attempt.AttemptId,
            AttemptIdName = attempt.AttemptIdName,
            AttemptIdSource = attempt.AttemptIdSource,
            TraceId = attempt.TraceId,
            TraceIdName = attempt.TraceIdName,
            TraceIdSource = attempt.TraceIdSource,
            State = state,
            CreatedOnUtc = attempt.CreatedOnUtc,
            Metadata = new Dictionary<string, string>(attempt.Metadata, StringComparer.OrdinalIgnoreCase)
        };

    private InboxEntry GetExisting(Ulid entryId)
        => _entriesById.TryGetValue(entryId, out var entry)
            ? entry
            : throw new InboxEntryNotFoundException(entryId);
}
