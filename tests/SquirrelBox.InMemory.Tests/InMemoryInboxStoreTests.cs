using SquirrelBox.InMemory;

namespace SquirrelBox.InMemory.Tests;

public sealed class InMemoryInboxStoreTests
{
    [Fact]
    public async Task TryBeginAsync_keys_entries_by_source_operation_and_idempotency_key()
    {
        var store = new InMemoryInboxStore();

        var first = await store.TryBeginAsync(Create("POST /orders"));
        var otherOperation = await store.TryBeginAsync(Create("POST /payments"));

        Assert.Equal(InboxBeginState.Started, first.State);
        Assert.Equal(InboxBeginState.Started, otherOperation.State);
        Assert.NotEqual(first.Entry.Id, otherOperation.Entry.Id);
    }

    [Fact]
    public async Task MarkCompletedAsync_throws_when_entry_is_unknown()
    {
        var store = new InMemoryInboxStore();

        await Assert.ThrowsAsync<InboxEntryNotFoundException>(() =>
            store.MarkCompletedAsync(Guid.NewGuid(), InboxCompletion.Empty, DateTimeOffset.UtcNow).AsTask());
    }

    private static InboxEntry Create(string operation)
        => new()
        {
            Id = Guid.NewGuid(),
            Source = "http",
            Operation = operation,
            IdempotencyKey = "same-key",
            PayloadHash = "abc",
            PayloadType = typeof(object).AssemblyQualifiedName,
            Status = InboxStatus.Started,
            CreatedOnUtc = DateTimeOffset.UtcNow,
            UpdatedOnUtc = DateTimeOffset.UtcNow
        };
}
