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

        return ValueTask.FromResult(new InboxBeginResult(MapDuplicateState(stored), stored));
    }

    public ValueTask MarkCompletedAsync(Guid entryId, DateTimeOffset completedOnUtc, CancellationToken cancellationToken = default)
    {
        if (_entriesById.TryGetValue(entryId, out var entry))
        {
            entry.Status = InboxStatus.Completed;
            entry.CompletedOnUtc = completedOnUtc;
            entry.UpdatedOnUtc = completedOnUtc;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask MarkFailedAsync(Guid entryId, string failure, DateTimeOffset failedOnUtc, CancellationToken cancellationToken = default)
    {
        if (_entriesById.TryGetValue(entryId, out var entry))
        {
            entry.Status = InboxStatus.Failed;
            entry.Failure = failure;
            entry.UpdatedOnUtc = failedOnUtc;
        }

        return ValueTask.CompletedTask;
    }

    private static string BuildKey(InboxEntry entry)
        => string.Join(":", entry.Source, entry.Operation, entry.IdempotencyKey);

    private static InboxBeginState MapDuplicateState(InboxEntry entry)
        => entry.Status switch
        {
            InboxStatus.Completed => InboxBeginState.DuplicateCompleted,
            InboxStatus.Failed => InboxBeginState.DuplicateFailed,
            _ => InboxBeginState.DuplicateInProgress
        };
}
