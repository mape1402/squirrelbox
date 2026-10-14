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

    [Fact]
    public async Task TryOpenAsync_returns_duplicate_completed_for_completed_entry()
    {
        var store = new InMemoryInboxStore();
        var entry = Create("POST /orders");
        var opened = await store.TryOpenAsync(entry);
        await store.MarkCompletedAsync(opened.Entry.Id, InboxCompletion.Empty, DateTimeOffset.UtcNow);

        var duplicate = await store.TryOpenAsync(Create("POST /orders"));

        Assert.Equal(InboxOpenState.DuplicateCompleted, duplicate.State);
        Assert.Equal(opened.Entry.Id, duplicate.Entry.Id);
    }

    [Fact]
    public async Task TryOpenAsync_detects_payload_conflict_before_reopening()
    {
        var store = new InMemoryInboxStore();
        var first = Create("POST /orders");
        first.PayloadHash = "abc";
        var second = Create("POST /orders");
        second.PayloadHash = "different";

        await store.TryOpenAsync(first);
        var conflict = await store.TryOpenAsync(second);

        Assert.Equal(InboxOpenState.PayloadConflict, conflict.State);
        Assert.Equal("abc", conflict.Entry.PayloadHash);
    }

    [Fact]
    public async Task TryOpenAsync_reopens_failed_entry_and_preserves_correlation_before_expiration()
    {
        var store = new InMemoryInboxStore();
        var first = Create("POST /orders", correlationId: "original-correlation");
        var opened = await store.TryOpenAsync(first);
        await store.MarkFailedAsync(opened.Entry.Id, new InboxFailure { Details = "failed" }, DateTimeOffset.UtcNow);
        var retry = Create("POST /orders", correlationId: "incoming-correlation");

        var reopened = await store.TryOpenAsync(retry);

        Assert.Equal(InboxOpenState.Opened, reopened.State);
        Assert.Equal(InboxStatus.Started, reopened.Entry.Status);
        Assert.Equal("original-correlation", reopened.Entry.CorrelationId);
        Assert.Null(reopened.Entry.Failure);
    }

    [Fact]
    public async Task TryOpenAsync_reopens_expired_completed_entry_unless_completed_lock_is_forever()
    {
        var store = new InMemoryInboxStore();
        var now = DateTimeOffset.UtcNow;
        var window = Create("POST /orders", createdOnUtc: now);
        window.ExpiresOnUtc = now.AddSeconds(1);
        var forever = Create("POST /payments", createdOnUtc: now);
        forever.ExpiresOnUtc = now.AddSeconds(1);
        forever.CompletedLock = InboxCompletedLockMode.Forever;

        var openedWindow = await store.TryOpenAsync(window);
        var openedForever = await store.TryOpenAsync(forever);
        await store.MarkCompletedAsync(openedWindow.Entry.Id, InboxCompletion.Empty, now);
        await store.MarkCompletedAsync(openedForever.Entry.Id, InboxCompletion.Empty, now);

        var reopened = await store.TryOpenAsync(Create("POST /orders", createdOnUtc: now.AddSeconds(2)));
        var locked = await store.TryOpenAsync(Create("POST /payments", createdOnUtc: now.AddSeconds(2)));

        Assert.Equal(InboxOpenState.Opened, reopened.State);
        Assert.Equal(InboxOpenState.DuplicateCompleted, locked.State);
    }

    [Fact]
    public async Task TryOpenAsync_reopens_active_entry_after_in_progress_timeout()
    {
        var store = new InMemoryInboxStore();
        var now = DateTimeOffset.UtcNow;
        var first = Create("POST /orders", createdOnUtc: now);
        first.UpdatedOnUtc = now;
        first.Deferred.InProgressTimeout = TimeSpan.FromSeconds(1);
        await store.TryOpenAsync(first);

        var reopened = await store.TryOpenAsync(Create("POST /orders", createdOnUtc: now.AddSeconds(2)));

        Assert.Equal(InboxOpenState.Opened, reopened.State);
    }

    [Fact]
    public async Task AttachPayloadHashAsync_attaches_verifies_and_detects_conflict()
    {
        var store = new InMemoryInboxStore();
        var entry = Create("POST /orders");
        entry.PayloadHash = null;
        var opened = await store.TryOpenAsync(entry);

        var attached = await store.AttachPayloadHashAsync(opened.Entry.Id, "abc");
        var verified = await store.AttachPayloadHashAsync(opened.Entry.Id, "abc");
        var conflict = await store.AttachPayloadHashAsync(opened.Entry.Id, "other");

        Assert.Equal(InboxPayloadVerificationState.Attached, attached.State);
        Assert.Equal(InboxPayloadVerificationState.Verified, verified.State);
        Assert.Equal(InboxPayloadVerificationState.PayloadConflict, conflict.State);
    }

    [Fact]
    public async Task MarkRetryingAsync_updates_status_and_query_filters_results()
    {
        var store = new InMemoryInboxStore();
        var first = Create("POST /orders", correlationId: "corr-1");
        var second = Create("POST /payments", correlationId: "corr-2");
        await store.TryOpenAsync(first);
        await store.TryOpenAsync(second);
        await store.MarkRetryingAsync(first.Id, new InboxFailure { Details = "retry" }, DateTimeOffset.UtcNow);

        var result = await store.QueryAsync(new InboxQuery
        {
            Status = InboxStatus.Retrying,
            Source = "http",
            Operation = "POST /orders",
            IdempotencyKey = "same-key",
            CorrelationId = "corr-1",
            Limit = 1
        });

        var entry = Assert.Single(result);
        Assert.Equal(first.Id, entry.Id);
        Assert.Equal("retry", entry.Failure);
    }

    private static InboxEntry Create(
        string operation,
        string correlationId = "correlation",
        DateTimeOffset? createdOnUtc = null)
        => new()
        {
            Id = Ulid.NewUlid(),
            Source = "http",
            Operation = operation,
            IdempotencyKey = "same-key",
            IdempotencyKeySource = InboxIdempotencyKeySource.Explicit,
            PayloadHash = "abc",
            PayloadType = typeof(object).AssemblyQualifiedName,
            CorrelationId = correlationId,
            Deferred = new InboxDeferredExecutionOptions(),
            Status = InboxStatus.Started,
            CreatedOnUtc = createdOnUtc ?? DateTimeOffset.UtcNow,
            UpdatedOnUtc = createdOnUtc ?? DateTimeOffset.UtcNow
        };
}
