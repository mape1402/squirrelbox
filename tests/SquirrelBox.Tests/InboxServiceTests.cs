using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.InMemory;

namespace SquirrelBox.Tests;

public sealed class InboxServiceTests
{
    [Fact]
    public async Task OpenOrContinueAsync_opens_context_for_explicit_key()
    {
        var inbox = CreateInbox();

        var result = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            source: "http",
            operation: "POST /orders",
            idempotencyKey: "order-1",
            payload: new TestPayload("order-1")));

        Assert.Equal(InboxOpenState.Opened, result.State);
        Assert.True(result.Accepted);
        Assert.Equal(result.Context, inbox.Current);
        Assert.NotEqual(default, result.Entry.Id);
        Assert.Equal("order-1", result.EffectiveIdempotencyKey);
        Assert.Equal(InboxIdempotencyKeySource.Explicit, result.IdempotencyKeySource);
    }

    [Fact]
    public async Task OpenOrContinueAsync_continues_current_context_without_registering_again()
    {
        var inbox = CreateInbox();
        var opened = await inbox.OpenOrContinueAsync(InboxOpenRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1")));

        var continued = await inbox.OpenOrContinueAsync(InboxOpenRequest.For("spider", "CreateOrderRequest", "other", new TestPayload("order-2")));

        Assert.Equal(InboxOpenState.Continued, continued.State);
        Assert.Equal(opened.Entry.Id, continued.Entry.Id);
    }

    [Fact]
    public async Task CompleteCurrentAsync_marks_completed_and_clears_context()
    {
        var inbox = CreateInbox();
        var opened = await inbox.OpenOrContinueAsync(InboxOpenRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1")));

        await inbox.CompleteCurrentAsync(new InboxCompletion
        {
            ContentType = "application/json",
            ResultPayload = [1, 2, 3]
        });

        var entry = await inbox.GetAsync(opened.Entry.Id);

        Assert.Null(inbox.Current);
        Assert.Equal(InboxStatus.Completed, entry.Status);
        Assert.Equal([1, 2, 3], entry.Completion.ResultPayload);
    }

    [Fact]
    public async Task FailCurrentAsync_marks_failed_and_clears_context()
    {
        var inbox = CreateInbox();
        var opened = await inbox.OpenOrContinueAsync(InboxOpenRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1")));

        await inbox.FailCurrentAsync(new InvalidOperationException("boom"));

        var entry = await inbox.GetAsync(opened.Entry.Id);

        Assert.Null(inbox.Current);
        Assert.Equal(InboxStatus.Failed, entry.Status);
        Assert.Equal("boom", entry.FailureDetails.ErrorMessage);
    }

    [Fact]
    public async Task OpenOrContinueAsync_detects_duplicate_in_progress_from_shared_store()
    {
        var provider = CreateProvider();
        var first = provider.GetRequiredService<IInboxService>();
        var second = provider.GetRequiredService<IInboxService>();

        await first.OpenOrContinueAsync(InboxOpenRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1")));
        await first.CompleteCurrentAsync();

        var duplicate = await second.OpenOrContinueAsync(InboxOpenRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1")));

        Assert.Equal(InboxOpenState.DuplicateCompleted, duplicate.State);
        Assert.False(duplicate.Accepted);
    }

    [Fact]
    public async Task OpenOrContinueAsync_reports_payload_conflict_for_same_key_with_different_payload()
    {
        var provider = CreateProvider();
        var first = provider.GetRequiredService<IInboxService>();
        var second = provider.GetRequiredService<IInboxService>();

        await first.OpenOrContinueAsync(InboxOpenRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1")));
        await first.CompleteCurrentAsync();

        var conflict = await second.OpenOrContinueAsync(InboxOpenRequest.For("http", "POST /orders", "order-1", new TestPayload("order-2")));

        Assert.Equal(InboxOpenState.PayloadConflict, conflict.State);
        Assert.False(conflict.Accepted);
    }

    [Fact]
    public async Task OpenOrContinueAsync_computes_idempotency_key_from_payload_when_missing()
    {
        var inbox = CreateInbox();

        var result = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            source: "spider",
            operation: "CreateOrderRequest",
            payload: new TestPayload("order-1")));

        Assert.Equal(InboxOpenState.Opened, result.State);
        Assert.False(string.IsNullOrWhiteSpace(result.EffectiveIdempotencyKey));
        Assert.Equal(result.Entry.PayloadHash, result.EffectiveIdempotencyKey);
        Assert.Equal(InboxIdempotencyKeySource.ComputedFromPayload, result.IdempotencyKeySource);
    }

    [Fact]
    public async Task OpenOrContinueAsync_returns_missing_key_when_no_key_or_payload_available()
    {
        var inbox = CreateInbox();

        var result = await inbox.OpenOrContinueAsync(new InboxOpenRequest
        {
            Source = "http",
            Operation = "POST /orders",
            AllowPayloadHashAsIdempotencyKey = true
        });

        Assert.Equal(InboxOpenState.MissingIdempotencyKey, result.State);
        Assert.False(result.Accepted);
        Assert.Null(inbox.Current);
    }

    [Fact]
    public async Task VerifyCurrentPayloadAsync_attaches_payload_hash_when_context_was_opened_without_payload()
    {
        var inbox = CreateInbox();
        var opened = await inbox.OpenOrContinueAsync(new InboxOpenRequest
        {
            Source = "http",
            Operation = "POST /orders",
            IdempotencyKey = "order-1"
        });

        var verification = await inbox.VerifyCurrentPayloadAsync(new TestPayload("order-1"));
        var entry = await inbox.GetAsync(opened.Entry.Id);

        Assert.Equal(InboxPayloadVerificationState.Attached, verification.State);
        Assert.False(string.IsNullOrWhiteSpace(entry.PayloadHash));
    }

    [Fact]
    public async Task VerifyCurrentPayloadAsync_detects_conflict_against_current_context()
    {
        var inbox = CreateInbox();

        await inbox.OpenOrContinueAsync(InboxOpenRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1")));

        var verification = await inbox.VerifyCurrentPayloadAsync(new TestPayload("order-2"));

        Assert.Equal(InboxPayloadVerificationState.PayloadConflict, verification.State);
        Assert.False(verification.Success);
    }

    [Fact]
    public async Task VerifyCurrentPayloadAsync_returns_no_current_context_when_none_is_open()
    {
        var inbox = CreateInbox();

        var verification = await inbox.VerifyCurrentPayloadAsync(new TestPayload("order-1"));

        Assert.Equal(InboxPayloadVerificationState.NoCurrentContext, verification.State);
    }

    [Fact]
    public async Task OpenOrContinueAsync_uses_default_lifetime_when_configured()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 9, 1, 0, 0, TimeSpan.Zero));
        var inbox = CreateInbox(new SquirrelBoxOptions { DefaultEntryLifetime = TimeSpan.FromMinutes(5) }, clock);

        var begin = await inbox.OpenOrContinueAsync(InboxOpenRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1")));

        Assert.Equal(clock.GetUtcNow().AddMinutes(5), begin.Entry.ExpiresOnUtc);
    }

    [Fact]
    public async Task OpenOrContinueAsync_uses_default_execution_mode_when_request_does_not_override_it()
    {
        var inbox = CreateInbox(new SquirrelBoxOptions { DefaultExecutionMode = InboxExecutionMode.Deferred }, new ManualTimeProvider(DateTimeOffset.UtcNow));

        var begin = await inbox.OpenOrContinueAsync(InboxOpenRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1")));

        Assert.Equal(InboxExecutionMode.Deferred, begin.Entry.ExecutionMode);
    }

    [Fact]
    public async Task OpenOrContinueAsync_reports_expired_for_duplicate_after_expiration()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 9, 1, 0, 0, TimeSpan.Zero));
        var provider = CreateProvider(new SquirrelBoxOptions { DefaultEntryLifetime = TimeSpan.FromMinutes(5) }, clock);
        var request = InboxOpenRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1"));

        using var firstScope = provider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IInboxService>();
        await first.OpenOrContinueAsync(request);
        clock.Advance(TimeSpan.FromMinutes(6));

        var duplicate = await RunWithoutAmbientContextAsync(async () =>
        {
            using var secondScope = provider.CreateScope();
            var second = secondScope.ServiceProvider.GetRequiredService<IInboxService>();
            return await second.OpenOrContinueAsync(request);
        });

        Assert.Equal(InboxOpenState.Expired, duplicate.State);
        Assert.Equal(InboxStatus.Expired, duplicate.Entry.Status);
        Assert.False(duplicate.Accepted);
    }

    [Fact]
    public async Task CompleteCurrentAsync_throws_when_no_context_exists()
    {
        var inbox = CreateInbox();

        await Assert.ThrowsAsync<InboxContextUnavailableException>(() =>
            inbox.CompleteCurrentAsync().AsTask());
    }

    [Fact]
    public async Task OpenOrContinueAsync_copies_request_metadata_to_entry()
    {
        var inbox = CreateInbox();
        var request = InboxOpenRequest.For("http", "POST /orders", "order-1", new TestPayload("order-1"));
        request.Metadata["tenant"] = "north";

        var begin = await inbox.OpenOrContinueAsync(request);

        Assert.Equal("north", begin.Entry.Metadata["tenant"]);
    }

    private static IInboxService CreateInbox()
        => CreateProvider().GetRequiredService<IInboxService>();

    private static IInboxService CreateInbox(SquirrelBoxOptions options, TimeProvider timeProvider)
        => CreateProvider(options, timeProvider).GetRequiredService<IInboxService>();

    private static ServiceProvider CreateProvider()
        => CreateProvider(new SquirrelBoxOptions(), new ManualTimeProvider(DateTimeOffset.UtcNow));

    private static ServiceProvider CreateProvider(SquirrelBoxOptions options, TimeProvider timeProvider)
    {
        var services = new ServiceCollection();
        services.AddSingleton(timeProvider);
        services.AddSquirrelBox(config =>
        {
            config.DefaultEntryLifetime = options.DefaultEntryLifetime;
            config.DefaultExecutionMode = options.DefaultExecutionMode;
            config.AllowPayloadHashAsIdempotencyKey = options.AllowPayloadHashAsIdempotencyKey;
            config.DefaultOwner = options.DefaultOwner;
        }).UseInMemory();

        return services.BuildServiceProvider();
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
}
