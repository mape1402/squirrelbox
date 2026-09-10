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
