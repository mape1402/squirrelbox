using SquirrelBox.InMemory;

namespace SquirrelBox.InMemory.Tests;

public sealed class InMemoryInboxStoreTests
{
    [Fact]
    public async Task TryOpenAsync_keys_entries_by_source_operation_and_idempotency_key()
    {
        var store = new InMemoryInboxStore();

        var first = await store.TryOpenAsync(Create("POST /orders"));
        var otherOperation = await store.TryOpenAsync(Create("POST /payments"));

        Assert.Equal(InboxOpenState.Opened, first.State);
        Assert.Equal(InboxOpenState.Opened, otherOperation.State);
        Assert.NotEqual(first.Entry.Id, otherOperation.Entry.Id);
    }

    [Fact]
    public async Task MarkCompletedAsync_throws_when_entry_is_unknown()
    {
        var store = new InMemoryInboxStore();

        await Assert.ThrowsAsync<InboxEntryNotFoundException>(() =>
            store.MarkCompletedAsync(Ulid.NewUlid(), InboxCompletion.Empty, DateTimeOffset.UtcNow).AsTask());
    }

    private static InboxEntry Create(string operation)
        => new()
        {
            Id = Ulid.NewUlid(),
            Source = "http",
            Operation = operation,
            IdempotencyKey = "same-key",
            IdempotencyKeySource = InboxIdempotencyKeySource.Explicit,
            PayloadHash = "abc",
            PayloadType = typeof(object).AssemblyQualifiedName,
            Status = InboxStatus.Started,
            CreatedOnUtc = DateTimeOffset.UtcNow,
            UpdatedOnUtc = DateTimeOffset.UtcNow
        };
}
