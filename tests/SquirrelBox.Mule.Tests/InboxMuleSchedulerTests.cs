using Microsoft.Extensions.DependencyInjection;
using Mule;
using NSubstitute;
using SquirrelBox.InMemory;

namespace SquirrelBox.Mule.Tests;

public sealed class InboxMuleSchedulerTests
{
    [Fact]
    public async Task EnqueueCurrentAsync_attaches_inbox_metadata_to_mule_action()
    {
        var mule = Substitute.For<IMuleClient>();
        EnqueueOptions capturedOptions = null;
        mule.EnqueueAsync(
                Arg.Any<ActionKey>(),
                Arg.Any<DeferredPayload>(),
                Arg.Do<Action<EnqueueOptions>>(configure =>
                {
                    capturedOptions = new EnqueueOptions();
                    configure(capturedOptions);
                }),
                Arg.Any<CancellationToken>())
            .Returns(new Guid("11111111-1111-1111-1111-111111111111"));

        var services = new ServiceCollection();
        services.AddSingleton(mule);
        services.AddSquirrelBox().UseInMemory();
        services.AddSquirrelBoxMule();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        var opened = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            "http",
            "POST /orders",
            "order-1",
            new DeferredPayload("order-1"),
            executionMode: InboxExecutionMode.Deferred));

        await scope.ServiceProvider.GetRequiredService<IInboxMuleScheduler>()
            .EnqueueCurrentAsync(ActionKey.From("tests.squirrelbox.deferred.v1"), new DeferredPayload("order-1"));

        Assert.Equal(opened.Entry.Id.ToString(), capturedOptions.Metadata["squirrelbox-inbox-id"]);
        Assert.Equal("order-1", capturedOptions.Metadata["idempotency-key"]);
    }

    private sealed record DeferredPayload(string Id);
}
