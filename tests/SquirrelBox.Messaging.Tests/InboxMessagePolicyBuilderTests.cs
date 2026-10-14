using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.InMemory;

namespace SquirrelBox.Messaging.Tests;

public sealed class InboxMessagePolicyBuilderTests
{
    [Fact]
    public async Task Policy_builder_applies_all_configured_values_to_opened_entry()
    {
        var services = new ServiceCollection();
        services
            .AddSquirrelBox(options => options.AddInboxPolicy("core-policy", _ => { }))
            .UseInMemory();
        services.AddSquirrelBoxMessaging(options =>
            options.ScanAssemblyContaining<InboxMessagePolicyBuilderTests>());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var opened = await scope.ServiceProvider.GetRequiredService<IInboxMessageService>()
            .OpenAsync(new InboxMessageContext
            {
                Transport = "rabbitmq",
                Topic = "builder-topic",
                Version = "9.9.9",
                Subscription = "builder-subscription",
                Operation = "builder-operation",
                Payload = new BuilderMessage("1")
            });

        Assert.True(opened.Enabled);
        Assert.Equal(InboxExecutionMode.Deferred, opened.OpenResult.Entry.ExecutionMode);
        Assert.Equal("core-policy", opened.OpenResult.Entry.PolicyName);
        Assert.Equal(TimeSpan.FromMinutes(7), opened.OpenResult.Entry.ExpiresOnUtc - opened.OpenResult.Entry.CreatedOnUtc);
        Assert.Equal(InboxCompletedLockMode.Forever, opened.OpenResult.Entry.CompletedLock);
        Assert.Equal("builder-lane", opened.OpenResult.Entry.Deferred.Lane);
        Assert.Equal(7, opened.OpenResult.Entry.Deferred.MaxAttempts);
    }

    [Fact]
    public void Policy_builder_rejects_null_deferred_callback()
    {
        var policies = new List<InboxMessagePolicy>();
        var builder = (InboxMessagePolicyProfileBuilder)Activator.CreateInstance(
            typeof(InboxMessagePolicyProfileBuilder),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            args: [policies],
            culture: null);

        Assert.Throws<ArgumentNullException>(() =>
            builder.ForAll().WithDeferred(null));
    }

    private sealed record BuilderMessage(string Id);
}

public sealed class BuilderPolicyProfile : InboxMessagePolicyProfile
{
    public override void Configure(InboxMessagePolicyProfileBuilder builder)
    {
        builder
            .ForTopic("builder-topic")
            .Named("friendly-name")
            .Version("9.9.9")
            .Subscription("builder-subscription")
            .Operation("builder-operation")
            .UseCorePolicy("core-policy")
            .WithEntryLifetime(TimeSpan.FromMinutes(7))
            .WithCompletedLock(InboxCompletedLockMode.Forever)
            .DeferExecution()
            .WithDeferred(options =>
            {
                options.Lane = "builder-lane";
                options.MaxAttempts = 7;
            })
            .AllowPayloadHashKeys();

        builder.Match(topic: "unused", version: "1", subscription: "sub", operation: "op");
    }
}
