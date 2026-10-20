using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SquirrelBox.InMemory;

namespace SquirrelBox.Tests;

public sealed class ConstructorGuardCoverageTests
{
    [Theory]
    [InlineData("options")]
    [InlineData("contextAccessor")]
    [InlineData("identityAccessor")]
    [InlineData("payloadHasher")]
    [InlineData("correlationIdResolver")]
    [InlineData("traceIdFactory")]
    [InlineData("attemptIdFactory")]
    [InlineData("events")]
    [InlineData("store")]
    [InlineData("timeProvider")]
    [InlineData("transactionRunner")]
    public void DefaultInbox_requires_constructor_dependencies(string dependency)
    {
        var services = CreateProvider();

        Assert.Throws<ArgumentNullException>(() => new DefaultInbox(
            dependency == "options" ? null : services.GetRequiredService<IOptions<SquirrelBoxOptions>>(),
            dependency == "contextAccessor" ? null : services.GetRequiredService<IInboxContextAccessor>(),
            dependency == "identityAccessor" ? null : services.GetRequiredService<ISquirrelBoxIdentityAccessor>(),
            dependency == "payloadHasher" ? null : services.GetRequiredService<IInboxPayloadHasher>(),
            dependency == "correlationIdResolver" ? null : services.GetRequiredService<ICorrelationIdResolver>(),
            dependency == "traceIdFactory" ? null : services.GetRequiredService<ITraceIdFactory>(),
            dependency == "attemptIdFactory" ? null : services.GetRequiredService<IAttemptIdFactory>(),
            dependency == "events" ? null : services.GetRequiredService<ISquirrelBoxEventPublisher>(),
            dependency == "store" ? null : services.GetRequiredService<IInboxStore>(),
            dependency == "timeProvider" ? null : services.GetRequiredService<TimeProvider>(),
            dependency == "transactionRunner" ? null : services.GetRequiredService<IInboxTransactionRunner>()));
    }

    [Theory]
    [InlineData("options")]
    [InlineData("contextAccessor")]
    [InlineData("profiles")]
    [InlineData("serializer")]
    [InlineData("payloadTypes")]
    [InlineData("store")]
    [InlineData("schedulers")]
    [InlineData("publishers")]
    [InlineData("events")]
    [InlineData("timeProvider")]
    public void DefaultOutboxService_requires_constructor_dependencies(string dependency)
    {
        var services = CreateProvider();

        Assert.Throws<ArgumentNullException>(() => new DefaultOutboxService(
            dependency == "options" ? null : services.GetRequiredService<IOptions<SquirrelBoxOptions>>(),
            dependency == "contextAccessor" ? null : services.GetRequiredService<IOutboxContextAccessor>(),
            dependency == "profiles" ? null : services.GetRequiredService<IOutboxProfileRegistry>(),
            dependency == "serializer" ? null : services.GetRequiredService<IOutboxEnvelopeSerializer>(),
            dependency == "payloadTypes" ? null : services.GetRequiredService<IOutboxPayloadTypeNameResolver>(),
            dependency == "store" ? null : services.GetRequiredService<IOutboxStore>(),
            dependency == "schedulers" ? null : Array.Empty<IOutboxDeferredScheduler>(),
            dependency == "publishers" ? null : Array.Empty<IOutboxTransportPublisher>(),
            dependency == "events" ? null : services.GetRequiredService<ISquirrelBoxEventPublisher>(),
            dependency == "timeProvider" ? null : services.GetRequiredService<TimeProvider>()));
    }

    [Theory]
    [InlineData("services")]
    [InlineData("inbox")]
    [InlineData("policyResolver")]
    [InlineData("registry")]
    [InlineData("deferredSchedulers")]
    public void OperationService_requires_constructor_dependencies(string dependency)
    {
        var services = CreateProvider();

        Assert.Throws<ArgumentNullException>(() => new DefaultSquirrelBoxOperationService(
            dependency == "services" ? null : services,
            dependency == "inbox" ? null : services.GetRequiredService<IInboxService>(),
            dependency == "policyResolver" ? null : services.GetRequiredService<IInboxPolicyResolver>(),
            dependency == "registry" ? null : services.GetRequiredService<ISquirrelBoxOperationRegistry>(),
            dependency == "deferredSchedulers" ? null : Array.Empty<ISquirrelBoxOperationDeferredScheduler>()));
    }

    [Fact]
    public void OperationRegistry_requires_options()
    {
        Assert.Throws<ArgumentNullException>(() => new DefaultSquirrelBoxOperationRegistry(null));
    }

    [Theory]
    [InlineData("services")]
    [InlineData("inbox")]
    public void OperationContext_requires_constructor_dependencies(string dependency)
    {
        var services = CreateProvider();

        Assert.Throws<ArgumentNullException>(() => new SquirrelBoxOperationContext(
            dependency == "services" ? null : services,
            dependency == "inbox" ? null : services.GetRequiredService<IInboxService>(),
            inboxContext: null));
    }

    [Theory]
    [InlineData("serializer")]
    [InlineData("payloadTypes")]
    [InlineData("options")]
    public void OutboxProfileContext_requires_constructor_dependencies(string dependency)
    {
        var services = CreateProvider();

        Assert.Throws<ArgumentNullException>(() => new OutboxProfileContext(
            dependency == "serializer" ? null : services.GetRequiredService<IOutboxEnvelopeSerializer>(),
            dependency == "payloadTypes" ? null : services.GetRequiredService<IOutboxPayloadTypeNameResolver>(),
            dependency == "options" ? null : new OutboxOptions(),
            DateTimeOffset.UtcNow));
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSquirrelBox().UseInMemory();
        services.AddScoped<ISquirrelBoxIdentityAccessor, AsyncLocalSquirrelBoxIdentityAccessor>();
        return services.BuildServiceProvider();
    }
}
