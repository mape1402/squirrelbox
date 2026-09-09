using System.Collections.Concurrent;

namespace SquirrelBox.InMemory;

public sealed class InMemoryInboxStore : IInboxStore
{
    private readonly ConcurrentDictionary<string, InboxEntry> _entriesByKey = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Guid, InboxEntry> _entriesById = new();

    public ValueTask<InboxBeginResult> TryBeginAsync(InboxEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var key = BuildKey(entry);
        var stored = _entriesByKey.GetOrAdd(key, _ =>
        {
            _entriesById[entry.Id] = entry;
            return entry;
        });

        if (ReferenceEquals(stored, entry))
            return ValueTask.FromResult(new InboxBeginResult(InboxBeginState.Started, stored));

        if (stored.ExpiresOnUtc is { } expiresOnUtc && expiresOnUtc <= entry.CreatedOnUtc)
        {
            stored.Status = InboxStatus.Expired;
            stored.UpdatedOnUtc = entry.CreatedOnUtc;
            return ValueTask.FromResult(new InboxBeginResult(InboxBeginState.Expired, stored));
        }

        if (!string.Equals(stored.PayloadHash, entry.PayloadHash, StringComparison.Ordinal))
            return ValueTask.FromResult(new InboxBeginResult(InboxBeginState.PayloadConflict, stored));

        return ValueTask.FromResult(new InboxBeginResult(MapDuplicateState(stored), stored));
    }

    public ValueTask MarkCompletedAsync(
        Guid entryId,
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

    public ValueTask MarkFailedAsync(
        Guid entryId,
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

    public ValueTask<InboxEntry> GetAsync(Guid entryId, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(GetExisting(entryId));

    private static string BuildKey(InboxEntry entry)
        => string.Join(":", entry.Source, entry.Operation, entry.IdempotencyKey);

    private static InboxBeginState MapDuplicateState(InboxEntry entry)
        => entry.Status switch
        {
            InboxStatus.Completed => InboxBeginState.DuplicateCompleted,
            InboxStatus.Failed => InboxBeginState.DuplicateFailed,
            InboxStatus.Expired => InboxBeginState.Expired,
            _ => InboxBeginState.DuplicateInProgress
        };

    private InboxEntry GetExisting(Guid entryId)
        => _entriesById.TryGetValue(entryId, out var entry)
            ? entry
            : throw new InboxEntryNotFoundException(entryId);
}
