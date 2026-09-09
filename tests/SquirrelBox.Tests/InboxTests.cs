using Microsoft.Extensions.Options;
using SquirrelBox.InMemory;

namespace SquirrelBox.Tests;

public sealed class InboxTests
{
    [Fact]
    public async Task BeginAsync_accepts_first_request_and_detects_duplicate_in_progress()
    {
        var inbox = CreateInbox();
        var request = InboxRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1"));

        var first = await inbox.BeginAsync(request);
        var duplicate = await inbox.BeginAsync(request);

        Assert.Equal(InboxBeginState.Started, first.State);
        Assert.Equal(InboxBeginState.DuplicateInProgress, duplicate.State);
        Assert.False(duplicate.Accepted);
        Assert.Equal(first.Entry.Id, duplicate.Entry.Id);
    }

    [Fact]
    public async Task BeginAsync_reports_duplicate_completed_after_completion()
    {
        var inbox = CreateInbox();
        var request = InboxRequest.For("pigeon", "orders.created:billing", "message-1", new TestPayload("order-1"));

        var first = await inbox.BeginAsync(request);
        await inbox.CompleteAsync(first.Entry.Id);

        var duplicate = await inbox.BeginAsync(request);

        Assert.Equal(InboxBeginState.DuplicateCompleted, duplicate.State);
    }

    [Fact]
    public async Task BeginAsync_reports_payload_conflict_for_same_key_with_different_payload()
    {
        var inbox = CreateInbox();

        await inbox.BeginAsync(InboxRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1")));
        var conflict = await inbox.BeginAsync(InboxRequest.For("http", "POST /orders", "order-1", new TestPayload("order-2")));

        Assert.Equal(InboxBeginState.PayloadConflict, conflict.State);
        Assert.False(conflict.Accepted);
    }

    [Fact]
    public async Task FailAsync_records_failure_details()
    {
        var inbox = CreateInbox();
        var begin = await inbox.BeginAsync(InboxRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1")));

        await inbox.FailAsync(begin.Entry.Id, new InvalidOperationException("boom"));

        var entry = await inbox.GetAsync(begin.Entry.Id);

        Assert.Equal(InboxStatus.Failed, entry.Status);
        Assert.Equal("boom", entry.FailureDetails.ErrorMessage);
        Assert.Contains(nameof(InvalidOperationException), entry.FailureDetails.ErrorType);
    }

    [Fact]
    public async Task CompleteAsync_records_completion_snapshot()
    {
        var inbox = CreateInbox();
        var begin = await inbox.BeginAsync(InboxRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1")));

        await inbox.CompleteAsync(
            begin.Entry.Id,
            new InboxCompletion
            {
                ResultType = "application/json",
                ContentType = "application/json",
                ResultPayload = [1, 2, 3]
            });

        var entry = await inbox.GetAsync(begin.Entry.Id);

        Assert.Equal(InboxStatus.Completed, entry.Status);
        Assert.Equal([1, 2, 3], entry.Completion.ResultPayload);
    }

    [Fact]
    public async Task BeginAsync_uses_default_lifetime_when_configured()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 9, 1, 0, 0, TimeSpan.Zero));
        var inbox = CreateInbox(new SquirrelBoxOptions { DefaultEntryLifetime = TimeSpan.FromMinutes(5) }, clock);

        var begin = await inbox.BeginAsync(InboxRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1")));

        Assert.Equal(clock.GetUtcNow().AddMinutes(5), begin.Entry.ExpiresOnUtc);
    }

    [Fact]
    public async Task BeginAsync_uses_default_execution_mode_when_request_does_not_override_it()
    {
        var inbox = CreateInbox(new SquirrelBoxOptions { DefaultExecutionMode = InboxExecutionMode.Deferred }, new ManualTimeProvider(DateTimeOffset.UtcNow));

        var begin = await inbox.BeginAsync(InboxRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1")));

        Assert.Equal(InboxExecutionMode.Deferred, begin.Entry.ExecutionMode);
    }

    [Fact]
    public async Task BeginAsync_reports_expired_for_duplicate_after_expiration()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 9, 1, 0, 0, TimeSpan.Zero));
        var inbox = CreateInbox(new SquirrelBoxOptions { DefaultEntryLifetime = TimeSpan.FromMinutes(5) }, clock);
        var request = InboxRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1"));

        await inbox.BeginAsync(request);
        clock.Advance(TimeSpan.FromMinutes(6));

        var duplicate = await inbox.BeginAsync(request);

        Assert.Equal(InboxBeginState.Expired, duplicate.State);
        Assert.Equal(InboxStatus.Expired, duplicate.Entry.Status);
        Assert.False(duplicate.Accepted);
    }

    [Fact]
    public async Task BeginAsync_copies_request_metadata_to_entry()
    {
        var inbox = CreateInbox();
        var request = InboxRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1"));
        request.Metadata["tenant"] = "north";

        var begin = await inbox.BeginAsync(request);

        Assert.Equal("north", begin.Entry.Metadata["tenant"]);
    }

    [Fact]
    public async Task CompleteAsync_throws_when_entry_does_not_exist()
    {
        var inbox = CreateInbox();

        await Assert.ThrowsAsync<InboxEntryNotFoundException>(() =>
            inbox.CompleteAsync(Guid.NewGuid()).AsTask());
    }

    private static IInbox CreateInbox()
        => CreateInbox(new SquirrelBoxOptions(), new ManualTimeProvider(DateTimeOffset.UtcNow));

    private static IInbox CreateInbox(SquirrelBoxOptions options, TimeProvider timeProvider)
        => new DefaultInbox(
            Options.Create(options),
            new JsonInboxPayloadHasher(),
            new InMemoryInboxStore(),
            timeProvider);

    private sealed record TestPayload(string Id);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value) => _utcNow = _utcNow.Add(value);
    }
}
