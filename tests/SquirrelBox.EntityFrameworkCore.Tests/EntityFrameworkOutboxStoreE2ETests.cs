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
        var provider = CreateProvider(connectionString);
        await EnsureDatabaseAsync(provider);

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

        var attempt = Assert.Single(await verifyScope.ServiceProvider
            .GetRequiredService<IOutboxAttemptDiagnosticsStore>()
            .QueryAttemptsAsync(new OutboxAttemptQuery { OutboxEnvelopeId = envelopeId }));
        Assert.Equal(OutboxAttemptState.Published, attempt.State);
        Assert.Equal(envelopeId, attempt.OutboxEnvelopeId);
        Assert.NotNull(attempt.CompletedOnUtc);
    }

    [Fact]
    public async Task SqlServer_combined_model_queries_inbox_and_outbox_history()
    {
        var connectionString = CreateIsolatedConnectionString();
        var provider = CreateProvider(connectionString);
        await EnsureDatabaseAsync(provider);

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

    [Fact]
    public async Task SqlServer_outbox_records_failed_publish_attempt()
    {
        var connectionString = CreateIsolatedConnectionString();
        var provider = CreateProvider(connectionString, publisherSucceeds: false);
        await EnsureDatabaseAsync(provider);

        Ulid envelopeId;
        using (var scope = provider.CreateScope())
        {
            var envelope = await scope.ServiceProvider.GetRequiredService<IOutboxService>()
                .EnqueueAsync(new OutboxEnqueueRequest
                {
                    Transport = "test",
                    Operation = "orders.created",
                    Destination = "orders",
                    Payload = new TestPayload("order-failed"),
                    TraceId = "trace-failed"
                });
            envelopeId = envelope.Id;
        }

        using (var publishScope = provider.CreateScope())
        {
            var result = await publishScope.ServiceProvider.GetRequiredService<IOutboxService>()
                .PublishAsync(envelopeId);
            Assert.False(result.Succeeded);
        }

        using var verifyScope = provider.CreateScope();
        var attempt = Assert.Single(await verifyScope.ServiceProvider
            .GetRequiredService<IOutboxAttemptDiagnosticsStore>()
            .QueryAttemptsAsync(new OutboxAttemptQuery { State = OutboxAttemptState.Failed }));

        Assert.Equal(envelopeId, attempt.OutboxEnvelopeId);
        Assert.Equal("trace-failed", attempt.TraceId);
        Assert.Equal("publisher rejected", attempt.Failure.Details);
    }

    [Fact]
    public async Task SqlServer_outbox_marks_discarded_and_creates_attempt_when_no_publish_attempt_exists()
    {
        var connectionString = CreateIsolatedConnectionString();
        var provider = CreateProvider(connectionString);
        await EnsureDatabaseAsync(provider);
        Ulid envelopeId;

        using (var scope = provider.CreateScope())
        {
            var envelope = await scope.ServiceProvider.GetRequiredService<IOutboxService>()
                .EnqueueAsync(new OutboxEnqueueRequest
                {
                    Transport = "test",
                    Operation = "orders.discarded",
                    Destination = "orders",
                    Payload = new TestPayload("discarded"),
                    TraceId = "trace-discarded"
                });
            envelopeId = envelope.Id;
        }

        using (var markScope = provider.CreateScope())
        {
            await markScope.ServiceProvider.GetRequiredService<IOutboxStore>()
                .MarkDiscardedAsync(envelopeId, DateTimeOffset.UtcNow);
        }

        using var verifyScope = provider.CreateScope();
        var stored = await verifyScope.ServiceProvider.GetRequiredService<IOutboxService>()
            .GetAsync(envelopeId);
        var attempt = Assert.Single(await verifyScope.ServiceProvider
            .GetRequiredService<IOutboxAttemptDiagnosticsStore>()
            .QueryAttemptsAsync(new OutboxAttemptQuery { OutboxEnvelopeId = envelopeId, TraceId = "trace-discarded" }));

        Assert.Equal(OutboxStatus.Discarded, stored.Status);
        Assert.Equal(OutboxAttemptState.Discarded, attempt.State);
        Assert.NotNull(attempt.CompletedOnUtc);
        Assert.Equal("orders.discarded", attempt.Metadata["operation"]);
    }

    [Fact]
    public async Task SqlServer_outbox_filters_queries_and_public_factory_constructor_reads_envelope()
    {
        var connectionString = CreateIsolatedConnectionString();
        var services = new ServiceCollection();
        services.AddSingleton(new TestOutboxPublisher(true));
        services.AddSingleton<IOutboxTransportPublisher>(provider => provider.GetRequiredService<TestOutboxPublisher>());
        services.AddDbContextFactory<TestSquirrelBoxDbContext>(options => options.UseSqlServer(connectionString));
        services.AddSquirrelBox().UseEntityFramework<TestSquirrelBoxDbContext>();
        await using var provider = services.BuildServiceProvider();
        await EnsureDatabaseAsync(provider);
        Ulid envelopeId;

        using (var scope = provider.CreateScope())
        {
            var envelope = await scope.ServiceProvider.GetRequiredService<IOutboxService>()
                .EnqueueAsync(new OutboxEnqueueRequest
                {
                    Transport = "factory",
                    Operation = "orders.filtered",
                    Destination = "orders",
                    Payload = new TestPayload("filtered"),
                    CorrelationId = "corr-filtered",
                    Headers = { ["h"] = "v" },
                    Metadata = { ["m"] = "v" }
                });
            envelopeId = envelope.Id;
        }

        var factory = provider.GetRequiredService<IDbContextFactory<TestSquirrelBoxDbContext>>();
        var store = new EntityFrameworkOutboxStore<TestSquirrelBoxDbContext>(factory);
        var byTransport = await store.QueryAsync(new OutboxQuery { Transport = "factory" });
        var byOperation = await store.QueryAsync(new OutboxQuery { Operation = "orders.filtered" });
        var byCorrelation = await store.QueryAsync(new OutboxQuery { CorrelationId = "corr-filtered", Limit = 0 });
        var byStatus = await store.QueryAsync(new OutboxQuery { Status = OutboxStatus.Pending });
        var stored = await store.GetAsync(envelopeId);

        Assert.Single(byTransport);
        Assert.Single(byOperation);
        Assert.Single(byCorrelation);
        Assert.Contains(byStatus, envelope => envelope.Id == envelopeId);
        Assert.Equal("v", stored.Headers["h"]);
        Assert.Equal("v", stored.Metadata["m"]);
    }

    private static ServiceProvider CreateProvider(string connectionString, bool publisherSucceeds = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TestOutboxPublisher(publisherSucceeds));
        services.AddSingleton<IOutboxTransportPublisher>(provider => provider.GetRequiredService<TestOutboxPublisher>());
        services.AddDbContext<TestSquirrelBoxDbContext>(options => options.UseSqlServer(connectionString));
        services
            .AddSquirrelBox()
            .UseEntityFramework<TestSquirrelBoxDbContext>();

        return services.BuildServiceProvider();
    }

    private static async Task EnsureDatabaseAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestSquirrelBoxDbContext>();

        Assert.NotNull(dbContext.Model.FindEntityType(typeof(SquirrelBoxInboxEntryRecord)));
        Assert.NotNull(dbContext.Model.FindEntityType(typeof(SquirrelBoxOutboxEnvelopeRecord)));
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
        private readonly bool _succeeds;

        public TestOutboxPublisher(bool succeeds)
        {
            _succeeds = succeeds;
        }

        public string Transport => "test";

        public IList<OutboxEnvelope> Published { get; } = [];

        public ValueTask<OutboxPublishResult> PublishAsync(
            OutboxEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Published.Add(envelope);
            return ValueTask.FromResult(_succeeds
                ? OutboxPublishResult.Success
                : OutboxPublishResult.Failed(new OutboxFailure { Details = "publisher rejected" }));
        }
    }

    private sealed class TestSquirrelBoxDbContext : DbContext
    {
        public TestSquirrelBoxDbContext(DbContextOptions<TestSquirrelBoxDbContext> options)
            : base(options)
        {
        }
    }
}
