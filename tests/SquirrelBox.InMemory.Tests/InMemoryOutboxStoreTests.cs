namespace SquirrelBox.InMemory.Tests;

public sealed class InMemoryOutboxStoreTests
{
    [Fact]
    public async Task QueryAsync_filters_envelopes_by_status_and_transport()
    {
        var store = new InMemoryOutboxStore();
        var pending = CreateEnvelope("pigeon", OutboxStatus.Pending);
        var published = CreateEnvelope("http", OutboxStatus.Published);

        await store.AddAsync(pending);
        await store.AddAsync(published);

        var result = await store.QueryAsync(new OutboxQuery
        {
            Status = OutboxStatus.Pending,
            Transport = "pigeon"
        });

        var envelope = Assert.Single(result);
        Assert.Equal(pending.Id, envelope.Id);
    }

    [Fact]
    public async Task AddAsync_rejects_duplicate_envelope_id()
    {
        var store = new InMemoryOutboxStore();
        var envelope = CreateEnvelope("pigeon", OutboxStatus.Pending);

        await store.AddAsync(envelope);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAsync(envelope).AsTask());
    }

    [Fact]
    public async Task GetAsync_throws_when_envelope_is_unknown()
    {
        var store = new InMemoryOutboxStore();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.GetAsync(Ulid.NewUlid()).AsTask());
    }

    [Fact]
    public async Task QueryAsync_filters_by_operation_correlation_and_limit()
    {
        var store = new InMemoryOutboxStore();
        var first = CreateEnvelope("pigeon", OutboxStatus.Pending);
        first.Operation = "orders.created";
        first.CorrelationId = "corr-1";
        first.CreatedOnUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        var second = CreateEnvelope("pigeon", OutboxStatus.Pending);
        second.Operation = "orders.created";
        second.CorrelationId = "corr-1";
        second.CreatedOnUtc = DateTimeOffset.UtcNow;
        var other = CreateEnvelope("pigeon", OutboxStatus.Pending);
        other.Operation = "orders.cancelled";
        other.CorrelationId = "corr-2";

        await store.AddAsync(first);
        await store.AddAsync(second);
        await store.AddAsync(other);

        var result = await store.QueryAsync(new OutboxQuery
        {
            Operation = "orders.created",
            CorrelationId = "corr-1",
            Limit = 1
        });

        var envelope = Assert.Single(result);
        Assert.Equal(second.Id, envelope.Id);
    }

    [Fact]
    public async Task MarkFailedAsync_increments_attempts_and_stores_failure()
    {
        var store = new InMemoryOutboxStore();
        var envelope = CreateEnvelope("pigeon", OutboxStatus.Pending);
        await store.AddAsync(envelope);

        await store.MarkFailedAsync(
            envelope.Id,
            new OutboxFailure { Details = "failed" },
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddSeconds(30));

        var stored = await store.GetAsync(envelope.Id);
        Assert.Equal(OutboxStatus.Failed, stored.Status);
        Assert.Equal(1, stored.Attempts);
        Assert.Equal("failed", stored.Failure.Details);
    }

    [Fact]
    public async Task Publish_lifecycle_records_outbox_attempt()
    {
        var store = new InMemoryOutboxStore();
        var envelope = CreateEnvelope("pigeon", OutboxStatus.Pending);
        envelope.TraceId = "trace-1";
        await store.AddAsync(envelope);

        var started = DateTimeOffset.UtcNow;
        var completed = started.AddSeconds(1);
        await store.MarkPublishingAsync(envelope.Id, started);
        await store.MarkPublishedAsync(envelope.Id, completed);

        var attempt = Assert.Single(await store.QueryAttemptsAsync(new OutboxAttemptQuery
        {
            OutboxEnvelopeId = envelope.Id
        }));

        Assert.Equal(OutboxAttemptState.Published, attempt.State);
        Assert.Equal("trace-1", attempt.TraceId);
        Assert.Equal(started, attempt.StartedOnUtc);
        Assert.Equal(completed, attempt.CompletedOnUtc);
        Assert.Equal("pigeon", attempt.Metadata["transport"]);
    }

    [Fact]
    public async Task MarkPublishingAsync_appends_attempts_for_retried_envelope()
    {
        var store = new InMemoryOutboxStore();
        var envelope = CreateEnvelope("pigeon", OutboxStatus.Pending);
        await store.AddAsync(envelope);

        await store.MarkPublishingAsync(envelope.Id, DateTimeOffset.UtcNow.AddSeconds(-1));
        await store.MarkPublishingAsync(envelope.Id, DateTimeOffset.UtcNow);

        var attempts = await store.QueryAttemptsAsync(new OutboxAttemptQuery
        {
            OutboxEnvelopeId = envelope.Id
        });

        Assert.Equal(2, attempts.Count);
        Assert.All(attempts, attempt => Assert.Equal(OutboxAttemptState.Publishing, attempt.State));
    }

    [Fact]
    public async Task Failed_publish_records_outbox_attempt_failure()
    {
        var store = new InMemoryOutboxStore();
        var envelope = CreateEnvelope("pigeon", OutboxStatus.Pending);
        await store.AddAsync(envelope);

        await store.MarkPublishingAsync(envelope.Id, DateTimeOffset.UtcNow);
        await store.MarkFailedAsync(
            envelope.Id,
            new OutboxFailure { Details = "broker down" },
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddSeconds(30));

        var attempt = Assert.Single(await store.QueryAttemptsAsync(new OutboxAttemptQuery
        {
            State = OutboxAttemptState.Failed
        }));

        Assert.Equal(envelope.Id, attempt.OutboxEnvelopeId);
        Assert.Equal("broker down", attempt.Failure.Details);
    }

    [Fact]
    public async Task Terminal_lifecycle_without_open_attempt_creates_terminal_attempt()
    {
        var store = new InMemoryOutboxStore();
        var published = CreateEnvelope("pigeon", OutboxStatus.Pending);
        published.TraceId = "trace-published";
        var discarded = CreateEnvelope("http", OutboxStatus.Pending);
        discarded.TraceId = "trace-discarded";
        await store.AddAsync(published);
        await store.AddAsync(discarded);

        await store.MarkPublishedAsync(published.Id, DateTimeOffset.UtcNow);
        await store.MarkDiscardedAsync(discarded.Id, DateTimeOffset.UtcNow);

        var publishedAttempt = Assert.Single(await store.QueryAttemptsAsync(new OutboxAttemptQuery
        {
            State = OutboxAttemptState.Published,
            TraceId = "trace-published"
        }));
        var discardedAttempt = Assert.Single(await store.QueryAttemptsAsync(new OutboxAttemptQuery
        {
            State = OutboxAttemptState.Discarded,
            TraceId = "trace-discarded"
        }));

        Assert.Equal(published.Id, publishedAttempt.OutboxEnvelopeId);
        Assert.Equal(discarded.Id, discardedAttempt.OutboxEnvelopeId);
    }

    [Fact]
    public async Task QueryAttemptsAsync_applies_limit_and_returns_metadata_clone()
    {
        var store = new InMemoryOutboxStore();
        var first = CreateEnvelope("pigeon", OutboxStatus.Pending);
        var second = CreateEnvelope("pigeon", OutboxStatus.Pending);
        await store.AddAsync(first);
        await store.AddAsync(second);

        await store.MarkPublishingAsync(first.Id, DateTimeOffset.UtcNow.AddMinutes(-1));
        await store.MarkPublishingAsync(second.Id, DateTimeOffset.UtcNow);

        var attempts = await store.QueryAttemptsAsync(new OutboxAttemptQuery { Limit = 1 });
        var attempt = Assert.Single(attempts);
        attempt.Metadata["transport"] = "mutated";
        var again = await store.QueryAttemptsAsync(new OutboxAttemptQuery
        {
            OutboxEnvelopeId = attempt.OutboxEnvelopeId
        });

        Assert.Equal(second.Id, attempt.OutboxEnvelopeId);
        Assert.Equal("pigeon", Assert.Single(again).Metadata["transport"]);
    }

    [Fact]
    public async Task Null_queries_return_all_envelopes_and_attempts()
    {
        var store = new InMemoryOutboxStore();
        var envelope = CreateEnvelope("pigeon", OutboxStatus.Pending);
        await store.AddAsync(envelope);
        await store.MarkPublishingAsync(envelope.Id, DateTimeOffset.UtcNow);

        Assert.Single(await store.QueryAsync(null));
        Assert.Single(await store.QueryAttemptsAsync(null));
    }

    private static OutboxEnvelope CreateEnvelope(string transport, OutboxStatus status)
        => new()
        {
            Id = Ulid.NewUlid(),
            Transport = transport,
            Operation = "orders.created",
            Destination = "orders",
            PayloadType = typeof(string).AssemblyQualifiedName,
            Payload = [1, 2, 3],
            ContentType = "application/json",
            Status = status,
            CreatedOnUtc = DateTimeOffset.UtcNow,
            UpdatedOnUtc = DateTimeOffset.UtcNow
        };
}
