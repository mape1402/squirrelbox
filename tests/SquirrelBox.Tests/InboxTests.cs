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

    private static IInbox CreateInbox()
        => new DefaultInbox(new JsonInboxPayloadHasher(), new InMemoryInboxStore());

    private sealed record TestPayload(string Id);
}
