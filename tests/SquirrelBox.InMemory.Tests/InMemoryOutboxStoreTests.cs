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
