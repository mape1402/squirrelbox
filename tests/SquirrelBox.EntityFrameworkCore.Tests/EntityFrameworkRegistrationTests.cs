using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.EntityFrameworkCore;

namespace SquirrelBox.EntityFrameworkCore.Tests;

public sealed class EntityFrameworkRegistrationTests
{
    [Fact]
    public void Entity_framework_registration_requires_dbcontext_registration_first()
    {
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() =>
            services.AddSquirrelBox().UseEntityFramework<RegistrationDbContext>());
    }

    [Fact]
    public void Entity_framework_registration_supports_prebuilt_dbcontext_options_instance()
    {
        var services = new ServiceCollection();
        var options = new DbContextOptionsBuilder<RegistrationDbContext>()
            .UseSqlServer("Server=(local);Database=SquirrelBoxRegistrationTests;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        services.AddSingleton(options);
        services.AddScoped<RegistrationDbContext>();

        services.AddSquirrelBox().UseEntityFrameworkInbox<RegistrationDbContext>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RegistrationDbContext>()
            .Model
            .FindEntityType(typeof(SquirrelBoxInboxEntryRecord)));
    }

    [Fact]
    public void Inbox_only_registration_resolves_inbox_services_without_outbox_store()
    {
        var services = CreateServices();

        services.AddSquirrelBox().UseEntityFrameworkInbox<RegistrationDbContext>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IInboxStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IInboxDiagnosticsStore>());
        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IOutboxStore>());
    }

    [Fact]
    public void Outbox_only_registration_resolves_outbox_services_without_inbox_store()
    {
        var services = CreateServices();

        services.AddSquirrelBox().UseEntityFrameworkOutbox<RegistrationDbContext>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IOutboxStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IOutboxAttemptDiagnosticsStore>());
        Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<IInboxStore>());
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddDbContext<RegistrationDbContext>(options =>
            options.UseSqlServer("Server=(local);Database=SquirrelBoxRegistrationTests;Trusted_Connection=True;TrustServerCertificate=True"));
        return services;
    }

    private sealed class RegistrationDbContext : DbContext
    {
        public RegistrationDbContext(DbContextOptions<RegistrationDbContext> options)
            : base(options)
        {
        }
    }
}
