using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.InMemory;

namespace SquirrelBox.Tests;

public sealed class SquirrelBoxRegistrationTests
{
    [Fact]
    public void AddSquirrelBox_Should_Return_Builder_For_Provider_Registration()
    {
        var services = new ServiceCollection();

        var builder = services.AddSquirrelBox();

        Assert.Same(services, builder.Services);
    }

    [Fact]
    public void AddSquirrelBox_Builder_Should_Register_InMemory_Storage()
    {
        var services = new ServiceCollection();

        services
            .AddSquirrelBox()
            .UseInMemory();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IInboxStore>());
        Assert.NotNull(provider.GetRequiredService<IOutboxStore>());
    }
}
