using System.Transactions;
using System.Threading;
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
        var provider = CreateProvider(connectionString);
        await EnsureDatabaseAsync(provider);
        var executions = 0;
        var firstOpened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = RunWithoutAmbientContextAsync(() => ExecuteProtectedWorkAsync(
            provider,
            () => Interlocked.Increment(ref executions),
            firstOpened,
            releaseFirst.Task));
        await firstOpened.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var duplicate = await RunWithoutAmbientContextAsync(() => ExecuteProtectedWorkAsync(
            provider,
            () => Interlocked.Increment(ref executions)));
        releaseFirst.SetResult();
        var accepted = await first;

        Assert.Equal(InboxOpenState.Opened, accepted.State);
        Assert.Equal(InboxOpenState.DuplicateInProgress, duplicate.State);
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
        var provider = CreateProvider(connectionString);
        await EnsureDatabaseAsync(provider);
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

    [Fact]
    public async Task SqlServer_store_records_attempts_and_preserves_correlation_for_duplicates()
    {
        var connectionString = CreateIsolatedConnectionString();
        var provider = CreateProvider(connectionString);
        await EnsureDatabaseAsync(provider);
        InboxOpenResult opened;

        using (var firstScope = provider.CreateScope())
        {
            var inbox = firstScope.ServiceProvider.GetRequiredService<IInboxService>();
            opened = await inbox.OpenOrContinueAsync(new InboxOpenRequest
            {
                Source = "http",
                Operation = "POST /orders",
                Payload = new TestPayload("order-attempts")
            });

            await inbox.CompleteCurrentAsync();
        }
        var openedIdempotencyKey = opened.EffectiveIdempotencyKey;
        var openedCorrelationId = opened.EffectiveCorrelationId;
        var openedAttemptId = opened.EffectiveAttemptId;
        var openedTraceId = opened.EffectiveTraceId;

        using var secondScope = provider.CreateScope();
        var duplicate = await secondScope.ServiceProvider
            .GetRequiredService<IInboxService>()
            .OpenOrContinueAsync(new InboxOpenRequest
            {
                Source = "http",
                Operation = "POST /orders",
                Payload = new TestPayload("order-attempts")
            });

        using var verificationScope = provider.CreateScope();
        var dbContext = verificationScope.ServiceProvider.GetRequiredService<TestInboxDbContext>();
        var attempts = await dbContext.Set<SquirrelBoxInboxAttemptRecord>()
            .Where(attempt => attempt.InboxEntryId == opened.Entry.Id.ToString())
            .OrderBy(attempt => attempt.CreatedOnUtc)
            .ToListAsync();

        Assert.Equal(InboxOpenState.DuplicateCompleted, duplicate.State);
        Assert.Equal(openedIdempotencyKey, duplicate.EffectiveIdempotencyKey);
        Assert.Equal(openedCorrelationId, duplicate.EffectiveCorrelationId);
        Assert.NotEqual(openedAttemptId, duplicate.EffectiveAttemptId);
        Assert.NotEqual(openedTraceId, duplicate.EffectiveTraceId);
        Assert.Equal(2, attempts.Count);
        Assert.Contains(attempts, attempt => attempt.AttemptId == openedAttemptId);
        Assert.Contains(attempts, attempt => attempt.AttemptId == duplicate.EffectiveAttemptId);
    }

    [Fact]
    public async Task SqlServer_store_reopens_expired_entry_without_unique_key_blocking_retry()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 9, 1, 0, 0, TimeSpan.Zero));
        var connectionString = CreateIsolatedConnectionString();
        var provider = CreateProvider(connectionString, clock);
        await EnsureDatabaseAsync(provider);
        var request = InboxOpenRequest.For(
            "http",
            "POST /orders",
            "order-window",
            new TestPayload("order-window"),
            entryLifetime: TimeSpan.FromMinutes(5));

        using (var firstScope = provider.CreateScope())
        {
            var inbox = firstScope.ServiceProvider.GetRequiredService<IInboxService>();
            await inbox.OpenOrContinueAsync(request);
            await inbox.CompleteCurrentAsync();
        }

        clock.Advance(TimeSpan.FromMinutes(6));
        using var secondScope = provider.CreateScope();
        var reopened = await secondScope.ServiceProvider
            .GetRequiredService<IInboxService>()
            .OpenOrContinueAsync(request);

        Assert.Equal(InboxOpenState.Opened, reopened.State);
        Assert.Equal(InboxStatus.Started, reopened.Entry.Status);
        Assert.True(reopened.Accepted);
    }

    private static async Task<InboxOpenResult> ExecuteProtectedWorkAsync(
        ServiceProvider provider,
        Action execute,
        TaskCompletionSource openedSignal = null,
        Task release = null)
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
            openedSignal?.SetResult();
            if (release is null)
                await Task.Delay(250);
            else
                await release;

            await inbox.CompleteCurrentAsync();
        }

        return result;
    }

    private static async Task<T> RunWithoutAmbientContextAsync<T>(Func<Task<T>> action)
    {
        Task<T> task;
        using (ExecutionContext.SuppressFlow())
        {
            task = Task.Run(action);
        }

        return await task;
    }

    private static ServiceProvider CreateProvider(string connectionString, TimeProvider timeProvider = null)
    {
        var services = new ServiceCollection();
        if (timeProvider is not null)
            services.AddSingleton(timeProvider);

        services.AddDbContext<TestInboxDbContext>(options => options.UseSqlServer(connectionString));
        services
            .AddSquirrelBox()
            .UseEntityFrameworkInbox<TestInboxDbContext>();

        return services.BuildServiceProvider();
    }

    private static async Task EnsureDatabaseAsync(ServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TestInboxDbContext>();

        Assert.NotNull(dbContext.Model.FindEntityType(typeof(SquirrelBoxInboxEntryRecord)));
        Assert.NotNull(dbContext.Model.FindEntityType(typeof(SquirrelBoxInboxAttemptRecord)));
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

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value) => _utcNow = _utcNow.Add(value);
    }

    private sealed class TestInboxDbContext : DbContext
    {
        public TestInboxDbContext(DbContextOptions<TestInboxDbContext> options)
            : base(options)
        {
        }
    }
}
