using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace SquirrelBox.EntityFrameworkCore.Tests;

public sealed class CorrelationIdResolutionSqlServerE2ETests
{
    [Fact]
    public async Task Scoped_strategy_resolves_related_operation_from_application_database()
    {
        var connectionString = CreateIsolatedConnectionString();
        await using var provider = CreateProvider(connectionString);

        try
        {
            await EnsureDatabaseAsync(provider);
            string correlationId;

            using (var firstScope = provider.CreateScope())
            {
                var inbox = firstScope.ServiceProvider.GetRequiredService<IInboxService>();
                var first = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
                    "http",
                    "POST /legacy/a",
                    "sql-a-123",
                    new LegacySqlRequest("123")));
                correlationId = first.EffectiveCorrelationId;

                var database = firstScope.ServiceProvider.GetRequiredService<LegacyCorrelationDbContext>();
                database.LegacyRequests.Add(new LegacyCorrelationRecord
                {
                    Id = "123",
                    CorrelationId = correlationId
                });
                await database.SaveChangesAsync();
                await inbox.CompleteCurrentAsync();
            }

            using (var secondScope = provider.CreateScope())
            {
                var second = await secondScope.ServiceProvider.GetRequiredService<IInboxService>()
                    .OpenOrContinueAsync(InboxOpenRequest.For(
                        "http",
                        "POST /legacy/b",
                        "sql-b-123",
                        new LegacySqlRequest("123"),
                        policyName: "legacy-related"));

                Assert.Equal(InboxOpenState.Opened, second.State);
                Assert.Equal(correlationId, second.EffectiveCorrelationId);
                Assert.Equal(SquirrelBoxMetadataValueSource.Resolved, second.Entry.CorrelationIdSource);
                Assert.Equal(
                    SqlCorrelationStrategy.StrategyName,
                    second.Entry.Metadata[SquirrelBoxMetadataNames.CorrelationIdResolver]);
            }
        }
        finally
        {
            using var cleanupScope = provider.CreateScope();
            await cleanupScope.ServiceProvider.GetRequiredService<LegacyCorrelationDbContext>()
                .Database.EnsureDeletedAsync();
        }
    }

    private static ServiceProvider CreateProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddDbContext<LegacyCorrelationDbContext>(options =>
            options.UseSqlServer(connectionString));
        services.AddScoped<ICorrelationIdResolutionStrategy, SqlCorrelationStrategy>();
        services
            .AddSquirrelBox(options =>
                options.AddInboxPolicy("legacy-related", policy =>
                    policy.CorrelationIdResolutionMode = CorrelationIdResolutionMode.ResolveRequired))
            .UseEntityFrameworkInbox<LegacyCorrelationDbContext>();
        return services.BuildServiceProvider();
    }

    private static async Task EnsureDatabaseAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<LegacyCorrelationDbContext>();

        Assert.NotNull(database.Model.FindEntityType(typeof(LegacyCorrelationRecord)));
        Assert.NotNull(database.Model.FindEntityType(typeof(SquirrelBoxInboxEntryRecord)));
        await database.Database.EnsureCreatedAsync();
    }

    private static string CreateIsolatedConnectionString()
    {
        var builder = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("SQUIRRELBOX_SQLSERVER")
            ?? "Server=localhost,11434;Database=SquirrelBoxTests;User Id=sa;Password=SquirrelBox_12345!;TrustServerCertificate=True;Encrypt=False")
        {
            InitialCatalog = $"SquirrelBoxCorrelation_{Environment.Version.Major}_{Ulid.NewUlid()}"
        };
        return builder.ConnectionString;
    }

    private sealed record LegacySqlRequest(string LegacyId);

    private sealed class SqlCorrelationStrategy(
        LegacyCorrelationDbContext database) : ICorrelationIdResolutionStrategy
    {
        public const string StrategyName = "legacy-sql-database";

        public string Name => StrategyName;

        public int Order => 0;

        public async ValueTask<CorrelationIdResolutionResult> TryResolveAsync(
            InboxIdentityFactoryContext context,
            CancellationToken cancellationToken = default)
        {
            if (context.Payload is not LegacySqlRequest request)
                return CorrelationIdResolutionResult.NotApplicable;

            var correlationId = await database.LegacyRequests
                .AsNoTracking()
                .Where(record => record.Id == request.LegacyId)
                .Select(record => record.CorrelationId)
                .SingleOrDefaultAsync(cancellationToken);

            return string.IsNullOrWhiteSpace(correlationId)
                ? CorrelationIdResolutionResult.NotFound
                : CorrelationIdResolutionResult.Resolved(correlationId);
        }
    }

    private sealed class LegacyCorrelationDbContext(
        DbContextOptions<LegacyCorrelationDbContext> options) : DbContext(options)
    {
        public DbSet<LegacyCorrelationRecord> LegacyRequests => Set<LegacyCorrelationRecord>();
    }

    private sealed class LegacyCorrelationRecord
    {
        public string Id { get; set; }

        public string CorrelationId { get; set; }
    }
}
