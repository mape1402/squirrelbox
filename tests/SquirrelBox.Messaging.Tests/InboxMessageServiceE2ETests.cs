using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.InMemory;

namespace SquirrelBox.Messaging.Tests;

public sealed class InboxMessageServiceE2ETests
{
    [Fact]
    public async Task OpenAsync_uses_metadata_key_and_attaches_key_to_reply_metadata()
    {
        var provider = CreateProvider();

        using var firstScope = provider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IInboxMessageService>();
        var opened = await first.OpenAsync(new InboxMessageContext
        {
            Transport = "rabbitmq",
            Topic = "orders",
            Version = "v1",
            Subscription = "billing",
            Operation = "created",
            Payload = new OrderMessage("order-1"),
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["idempotency-key"] = "message-key"
            }
        });

        var replyMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        first.AttachEffectiveKey(replyMetadata);
        await firstScope.ServiceProvider.GetRequiredService<IInboxService>().CompleteCurrentAsync();

        using var secondScope = provider.CreateScope();
        var duplicate = await secondScope.ServiceProvider
            .GetRequiredService<IInboxMessageService>()
            .OpenAsync(new InboxMessageContext
            {
                Transport = "rabbitmq",
                Topic = "orders",
                Version = "v1",
                Subscription = "billing",
                Operation = "created",
                Payload = new OrderMessage("order-1"),
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["idempotency-key"] = "message-key"
                }
            });

        Assert.True(opened.ShouldExecute);
        Assert.Equal("message-key", replyMetadata["idempotency-key"]);
        Assert.Equal(InboxOpenState.DuplicateCompleted, duplicate.OpenResult.State);
        Assert.Equal("orders:v1/billing/created", duplicate.OpenResult.Entry.Operation);
    }

    [Fact]
    public async Task OpenAsync_computes_payload_key_when_metadata_and_message_id_are_missing()
    {
        var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInboxMessageService>();

        var opened = await service.OpenAsync(new InboxMessageContext
        {
            Transport = "rabbitmq",
            Topic = "orders",
            Version = "v2",
            Subscription = "shipping",
            Operation = "created",
            Payload = new OrderMessage("order-2")
        });

        var replyMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        service.AttachEffectiveKey(replyMetadata);

        Assert.Equal(InboxOpenState.Opened, opened.OpenResult.State);
        Assert.Equal(InboxIdempotencyKeySource.ComputedFromPayload, opened.OpenResult.IdempotencyKeySource);
        Assert.Equal(opened.EffectiveIdempotencyKey, replyMetadata["idempotency-key"]);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSquirrelBox().UseInMemory();
        services.AddSquirrelBoxMessaging();
        return services.BuildServiceProvider();
    }

    private sealed record OrderMessage(string Id);
}
