using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pigeon.Messaging.Consuming.Dispatching;
using Pigeon.Messaging.Producing;
using SquirrelBox.Messaging.Pigeon;

namespace SquirrelBox.Messaging.Tests;

public sealed class SquirrelBoxPigeonRegistrationTests
{
    [Fact]
    public void AddSquirrelBoxPigeon_registers_interceptors_publishers_and_options()
    {
        var services = new ServiceCollection();

        services.AddSquirrelBoxPigeon(options =>
        {
            options.Transport = "custom-pigeon";
            options.EnableOutbox = true;
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<SquirrelBoxPigeonOptions>>().Value;

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IConsumeDecisionInterceptor) &&
            descriptor.ImplementationType == typeof(SquirrelBoxPigeonDecisionInterceptor));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IConsumeExecutionInterceptor) &&
            descriptor.ImplementationType == typeof(SquirrelBoxPigeonExecutionInterceptor));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IPublishDecisionInterceptor) &&
            descriptor.ImplementationType == typeof(SquirrelBoxPigeonOutboxInterceptor));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IOutboxTransportPublisher) &&
            descriptor.ImplementationType == typeof(SquirrelBoxPigeonOutboxPublisher));
        Assert.Equal("custom-pigeon", options.Transport);
        Assert.True(options.EnableOutbox);
    }

    [Fact]
    public void AddSquirrelBoxPigeon_supports_default_options_registration()
    {
        var services = new ServiceCollection();

        services.AddSquirrelBoxPigeon();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IOptions<SquirrelBoxPigeonOptions>>().Value);
    }
}
