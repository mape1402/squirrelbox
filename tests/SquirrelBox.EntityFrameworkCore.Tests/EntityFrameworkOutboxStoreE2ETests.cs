using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.EntityFrameworkCore;

namespace SquirrelBox.EntityFrameworkCore.Tests;

public sealed class EntityFrameworkOutboxStoreE2ETests
{
    [Fact]
    public async Task SqlServer_outbox_persists_and_publishes_envelope()
    {
        var connectionString = CreateIsolatedConnectionString();
        await EnsureDatabaseAsync(connectionString);
        var provider = CreateProvider(connectionString);

        Ulid envelopeId;
        using (var scope = provider.CreateScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();
            var envelope = await outbox.EnqueueAsync(new OutboxEnqueueRequest
            {
                Transport = "test",
                Operation = "orders.created",
                Destination = "orders",
                Payload = new TestPayload("order-ef"),
                CorrelationId = "trace-ef"
            });

            envelopeId = envelope.Id;
        }

        using (var publishScope = provider.CreateScope())
        {
            await publishScope.ServiceProvider.GetRequiredService<IOutboxService>()
                .PublishAsync(envelopeId);
        }

        using var verifyScope = provider.CreateScope();
        var stored = await verifyScope.ServiceProvider.GetRequiredService<IOutboxService>()
            .GetAsync(envelopeId);
        var publisher = provider.GetRequiredService<TestOutboxPublisher>();

        Assert.Equal(OutboxStatus.Published, stored.Status);
        Assert.Equal(envelopeId, publisher.Published.Single().Id);
    }

    [Fact]
    public async Task SqlServer_combined_model_queries_inbox_and_outbox_history()
    {
        var connectionString = CreateIsolatedConnectionString();
        await EnsureDatabaseAsync(connectionString);
        var provider = CreateProvider(connectionString);

        using (var scope = provider.CreateScope())
        {
            var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
            await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
                "http",
                "POST /orders",
                "history-key",
                new TestPayload("history-key"),
                correlationId: "history"));
            await inbox.CompleteCurrentAsync();

            await scope.ServiceProvider.GetRequiredService<IOutboxService>()
                .EnqueueAsync(new OutboxEnqueueRequest
                {
                    Transport = "test",
                    Operation = "orders.created",
                    Destination = "orders",
                    Payload = new TestPayload("history-key"),
                    CorrelationId = "history"
                });
        }

        using var verifyScope = provider.CreateScope();
        var inboxEntries = await verifyScope.ServiceProvider.GetRequiredService<IInboxDiagnosticsStore>()
            .QueryAsync(new InboxQuery { CorrelationId = "history" });
        var outboxEntries = await verifyScope.ServiceProvider.GetRequiredService<IOutboxStore>()
            .QueryAsync(new OutboxQuery { CorrelationId = "history" });

        Assert.Single(inboxEntries);
        Assert.Single(outboxEntries);
    }

    private static ServiceProvider CreateProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TestOutboxPublisher>();
        services.AddSingleton<IOutboxTransportPublisher>(provider => provider.GetRequiredService<TestOutboxPublisher>());
        services.AddDbContextFactory<TestSquirrelBoxDbContext>(options => options.UseSqlServer(connectionString));
        services
            .AddSquirrelBox()
            .UseEntityFramework<TestSquirrelBoxDbContext>();

        return services.BuildServiceProvider();
    }

    private static async Task EnsureDatabaseAsync(string connectionString)
    {
        await using var dbContext = new TestSquirrelBoxDbContext(
            new DbContextOptionsBuilder<TestSquirrelBoxDbContext>()
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

    private sealed class TestOutboxPublisher : IOutboxTransportPublisher
    {
        public string Transport => "test";

        public IList<OutboxEnvelope> Published { get; } = [];

        public ValueTask<OutboxPublishResult> PublishAsync(
            OutboxEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Published.Add(envelope);
            return ValueTask.FromResult(OutboxPublishResult.Success);
        }
    }

    private sealed class TestSquirrelBoxDbContext : DbContext
    {
        public TestSquirrelBoxDbContext(DbContextOptions<TestSquirrelBoxDbContext> options)
            : base(options)
        {
        }

        public DbSet<SquirrelBoxInboxEntryRecord> InboxEntries => Set<SquirrelBoxInboxEntryRecord>();

        public DbSet<SquirrelBoxOutboxEnvelopeRecord> OutboxEnvelopes => Set<SquirrelBoxOutboxEnvelopeRecord>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
            => modelBuilder.ApplySquirrelBox();
    }
}
