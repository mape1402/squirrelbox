using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pigeon.Messaging.Consuming.Dispatching;
using Pigeon.Messaging.Contracts;
using SquirrelBox.InMemory;
using SquirrelBox.Messaging.Pigeon;

namespace SquirrelBox.Messaging.Tests;

public sealed class SquirrelBoxPigeonConsumeInterceptorTests
{
    [Fact]
    public async Task Intercept_opens_inbox_from_pigeon_consume_context_metadata()
    {
        var services = new ServiceCollection();
        services.AddSquirrelBox().UseInMemory();
        services.AddSquirrelBoxMessaging();
        services.AddSingleton(Options.Create(new SquirrelBoxPigeonOptions()));

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonConsumeInterceptor>(scope.ServiceProvider);

        await interceptor.Intercept(new ConsumeContext
        {
            Services = scope.ServiceProvider,
            Topic = "orders",
            Subscription = "billing",
            MessageVersion = SemanticVersion.Parse("1.2.0"),
            Message = new OrderMessage("order-1"),
            MessageType = typeof(OrderMessage),
            RawMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["idempotency-key"] = "pigeon-key",
                ["message-id"] = "broker-id"
            }
        });

        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        Assert.NotNull(inbox.Current);
        Assert.Equal("pigeon-key", inbox.Current.EffectiveIdempotencyKey);
        Assert.Equal("orders:1.2.0/billing/OrderMessage", inbox.Current.Entry.Operation);
    }

    private sealed record OrderMessage(string Id);
}
