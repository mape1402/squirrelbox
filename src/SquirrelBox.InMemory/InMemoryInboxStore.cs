using System.Collections.Concurrent;

namespace SquirrelBox.InMemory;

/// <summary>
/// In-memory implementation of <see cref="IInboxStore"/> for tests and local scenarios.
/// </summary>
public sealed class InMemoryInboxStore : IInboxStore, IInboxDiagnosticsStore
{
    private readonly ConcurrentDictionary<string, InboxEntry> _entriesByKey = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Ulid, InboxEntry> _entriesById = new();

    /// <inheritdoc />
    public ValueTask<InboxOpenResult> TryOpenAsync(InboxEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var key = BuildKey(entry);
        var stored = _entriesByKey.GetOrAdd(key, _ =>
        {
            _entriesById[entry.Id] = entry;
            return entry;
        });

        if (ReferenceEquals(stored, entry))
            return ValueTask.FromResult(new InboxOpenResult(InboxOpenState.Opened, entry: stored));

        if (stored.ExpiresOnUtc is { } expiresOnUtc && expiresOnUtc <= entry.CreatedOnUtc)
        {
            stored.Status = InboxStatus.Expired;
            stored.UpdatedOnUtc = entry.CreatedOnUtc;
            return ValueTask.FromResult(new InboxOpenResult(InboxOpenState.Expired, entry: stored));
        }

        if (!string.IsNullOrWhiteSpace(stored.PayloadHash) &&
            !string.IsNullOrWhiteSpace(entry.PayloadHash) &&
            !string.Equals(stored.PayloadHash, entry.PayloadHash, StringComparison.Ordinal))
        {
            return ValueTask.FromResult(new InboxOpenResult(InboxOpenState.PayloadConflict, entry: stored));
        }

        if (string.IsNullOrWhiteSpace(stored.PayloadHash) && !string.IsNullOrWhiteSpace(entry.PayloadHash))
            stored.PayloadHash = entry.PayloadHash;

        return ValueTask.FromResult(new InboxOpenResult(MapDuplicateState(stored), entry: stored));
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

    private InboxEntry GetExisting(Ulid entryId)
        => _entriesById.TryGetValue(entryId, out var entry)
            ? entry
            : throw new InboxEntryNotFoundException(entryId);
}
