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
        var openedCorrelationId = opened.EffectiveCorrelationId;
        var openedAttemptId = opened.EffectiveAttemptId;
        var openedTraceId = opened.EffectiveTraceId;
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
        Assert.Equal(openedCorrelationId, replyMetadata["correlation-id"]);
        Assert.Equal(openedAttemptId, replyMetadata["attempt-id"]);
        Assert.Equal(openedTraceId, replyMetadata["trace-id"]);
        Assert.Equal(InboxOpenState.DuplicateCompleted, duplicate.OpenResult.State);
        Assert.Equal("orders:v1/billing/created", duplicate.OpenResult.Entry.Operation);
        Assert.Equal(openedCorrelationId, duplicate.EffectiveCorrelationId);
        Assert.NotEqual(openedAttemptId, duplicate.EffectiveAttemptId);
        Assert.NotEqual(openedTraceId, duplicate.EffectiveTraceId);
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
        Assert.Equal(opened.EffectiveCorrelationId, replyMetadata["correlation-id"]);
        Assert.Equal(opened.EffectiveAttemptId, replyMetadata["attempt-id"]);
        Assert.Equal(opened.EffectiveTraceId, replyMetadata["trace-id"]);
    }

    [Fact]
    public async Task OpenAsync_preserves_custom_metadata_names_for_identity_propagation()
    {
        var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInboxMessageService>();

        var opened = await service.OpenAsync(new InboxMessageContext
        {
            Transport = "rabbitmq",
            Topic = "orders",
            Version = "v1",
            Subscription = "billing",
            Operation = "created",
            Payload = new OrderMessage("order-custom"),
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["x-idempotency-key"] = "custom-key",
                ["x-correlation-id"] = "corr-custom",
                ["x-trace-id"] = "trace-custom"
            }
        });

        var replyMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        service.AttachEffectiveKey(replyMetadata);

        Assert.Equal("custom-key", opened.EffectiveMetadata.IdempotencyKey.Value);
        Assert.Equal("corr-custom", opened.EffectiveMetadata.CorrelationId.Value);
        Assert.Equal("trace-custom", opened.EffectiveMetadata.TraceId.Value);
        Assert.Equal("custom-key", replyMetadata["x-idempotency-key"]);
        Assert.Equal("corr-custom", replyMetadata["x-correlation-id"]);
        Assert.Equal("trace-custom", replyMetadata["x-trace-id"]);
        Assert.Equal(opened.EffectiveAttemptId, replyMetadata["attempt-id"]);
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
