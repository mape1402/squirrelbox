using System.Transactions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.EntityFrameworkCore;

namespace SquirrelBox.EntityFrameworkCore.Tests;

public sealed class EntityFrameworkInboxStoreE2ETests
{
    [Fact]
    public async Task SqlServer_store_reserves_before_business_execution_and_deduplicates_concurrent_requests()
    {
        var connectionString = CreateIsolatedConnectionString();
        await EnsureDatabaseAsync(connectionString);

        var provider = CreateProvider(connectionString);
        var executions = 0;

        var first = Task.Run(() => ExecuteProtectedWorkAsync(provider, () => Interlocked.Increment(ref executions)));
        var second = Task.Run(() => ExecuteProtectedWorkAsync(provider, () => Interlocked.Increment(ref executions)));

        var results = await Task.WhenAll(first, second);
        var accepted = results.Single(result => result.State == InboxOpenState.Opened);
        var duplicate = results.Single(result => result.State == InboxOpenState.DuplicateInProgress);

        Assert.Equal(1, executions);
        Assert.NotEqual(default, accepted.Entry.Id);
        Assert.Equal(accepted.EffectiveIdempotencyKey, duplicate.EffectiveIdempotencyKey);

        using var scope = provider.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        var entry = await inbox.GetAsync(accepted.Entry.Id);

        Assert.Equal(InboxStatus.Completed, entry.Status);
    }

    [Fact]
    public async Task SqlServer_store_suppresses_ambient_transaction_when_opening_entry()
    {
        var connectionString = CreateIsolatedConnectionString();
        await EnsureDatabaseAsync(connectionString);
        var provider = CreateProvider(connectionString);
        Ulid entryId;

        using (var transaction = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            using var scope = provider.CreateScope();
            var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
            var opened = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
                "http",
                "POST /orders",
                "tx-key",
                new TestPayload("tx-key")));

            entryId = opened.Entry.Id;
        }

        using var verificationScope = provider.CreateScope();
        var verificationInbox = verificationScope.ServiceProvider.GetRequiredService<IInboxService>();
        var stored = await verificationInbox.GetAsync(entryId);

        Assert.Equal(InboxStatus.Started, stored.Status);
    }

    private static async Task<InboxOpenResult> ExecuteProtectedWorkAsync(ServiceProvider provider, Action execute)
    {
        using var scope = provider.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        var result = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            "http",
            "POST /orders",
            "order-1",
            new TestPayload("order-1")));

        if (result.State == InboxOpenState.Opened)
        {
            execute();
            await Task.Delay(250);
            await inbox.CompleteCurrentAsync();
        }

        return result;
    }

    private static ServiceProvider CreateProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContextFactory<TestInboxDbContext>(options => options.UseSqlServer(connectionString));
        services
            .AddSquirrelBox()
            .UseEntityFrameworkInbox<TestInboxDbContext>();

        return services.BuildServiceProvider();
    }

    private static async Task EnsureDatabaseAsync(string connectionString)
    {
        await using var dbContext = new TestInboxDbContext(
            new DbContextOptionsBuilder<TestInboxDbContext>()
                .UseSqlServer(connectionString)
                .Options);

        await dbContext.Database.EnsureCreatedAsync();
    }

    private static string CreateIsolatedConnectionString()
    {
        var builder = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("SQUIRRELBOX_SQLSERVER")
            ?? "Server=localhost,11434;Database=SquirrelBoxTests;User Id=sa;Password=SquirrelBox_12345!;TrustServerCertificate=True;Encrypt=False");

        builder.InitialCatalog = $"SquirrelBoxTests_{Environment.Version.Major}_{Ulid.NewUlid()}";
        return builder.ConnectionString;
    }

    private sealed record TestPayload(string Id);

    private sealed class TestInboxDbContext : DbContext
    {
        public TestInboxDbContext(DbContextOptions<TestInboxDbContext> options)
            : base(options)
        {
        }

        public DbSet<SquirrelBoxInboxEntryRecord> InboxEntries => Set<SquirrelBoxInboxEntryRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ApplySquirrelBoxInbox();
    }
}
