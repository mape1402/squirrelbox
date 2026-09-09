using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.InMemory;

namespace SquirrelBox.Tests;

public sealed class InboxEndToEndTests
{
    [Fact]
    public async Task Http_with_explicit_key_can_open_then_spider_can_continue_and_verify_payload()
    {
        var inbox = CreateInbox();

        var httpOpen = await inbox.OpenOrContinueAsync(new InboxOpenRequest
        {
            Source = "http",
            Operation = "POST /orders",
            IdempotencyKey = "client-key-1",
            Owner = "http"
        });

        var spiderOpen = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            source: "spider",
            operation: nameof(CreateOrderCommand),
            payload: new CreateOrderCommand("order-1"),
            owner: "spider"));
        var verification = await inbox.VerifyCurrentPayloadAsync(new CreateOrderCommand("order-1"));

        await inbox.CompleteCurrentAsync(new InboxCompletion
        {
            ContentType = "application/json",
            ResultPayload = [123]
        });

        var completed = await inbox.GetAsync(httpOpen.Entry.Id);

        Assert.Equal(InboxOpenState.Opened, httpOpen.State);
        Assert.Equal("client-key-1", httpOpen.EffectiveIdempotencyKey);
        Assert.Equal(InboxOpenState.Continued, spiderOpen.State);
        Assert.Equal(InboxPayloadVerificationState.Attached, verification.State);
        Assert.Equal(InboxStatus.Completed, completed.Status);
    }

    [Fact]
    public async Task Http_without_header_can_let_spider_compute_key_from_deserialized_payload()
    {
        var inbox = CreateInbox();

        var spiderOpen = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            source: "spider",
            operation: nameof(CreateOrderCommand),
            payload: new CreateOrderCommand("order-1"),
            owner: "spider"));
        var responseHeaderValue = spiderOpen.EffectiveIdempotencyKey;

        await inbox.CompleteCurrentAsync();

        var duplicate = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            source: "spider",
            operation: nameof(CreateOrderCommand),
            payload: new CreateOrderCommand("order-1"),
            owner: "spider"));

        Assert.Equal(InboxOpenState.Opened, spiderOpen.State);
        Assert.False(string.IsNullOrWhiteSpace(responseHeaderValue));
        Assert.Equal(InboxIdempotencyKeySource.ComputedFromPayload, spiderOpen.IdempotencyKeySource);
        Assert.Equal(InboxOpenState.DuplicateCompleted, duplicate.State);
    }

    [Fact]
    public async Task Messaging_without_message_id_can_compute_key_and_make_it_available_for_metadata()
    {
        var inbox = CreateInbox();

        var open = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            source: "messaging",
            operation: "orders.created:1.0.0/billing",
            payload: new OrderCreatedMessage("order-1"),
            correlationId: "corr-1",
            owner: "messaging"));
        var outboundMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["idempotency-key"] = open.EffectiveIdempotencyKey
        };

        await inbox.CompleteCurrentAsync();

        Assert.Equal(InboxOpenState.Opened, open.State);
        Assert.Equal(InboxIdempotencyKeySource.ComputedFromPayload, open.IdempotencyKeySource);
        Assert.Equal(open.Entry.PayloadHash, outboundMetadata["idempotency-key"]);
    }

    private static IInboxService CreateInbox()
    {
        var services = new ServiceCollection();
        services.AddSquirrelBox().UseInMemory();
        return services.BuildServiceProvider().GetRequiredService<IInboxService>();
    }

    private sealed record CreateOrderCommand(string OrderId);

    private sealed record OrderCreatedMessage(string OrderId);
}
