using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SquirrelBox.EntityFrameworkCore;

/// <summary>
/// Entity Framework Core implementation of <see cref="IOutboxStore"/>.
/// </summary>
/// <typeparam name="TDbContext">The DbContext type used to persist outbox envelopes.</typeparam>
public sealed class EntityFrameworkOutboxStore<TDbContext> : IOutboxStore
    where TDbContext : DbContext
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISquirrelBoxDbContextFactory<TDbContext> _dbContextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkOutboxStore{TDbContext}"/> class.
    /// </summary>
    /// <param name="dbContextFactory">The DbContext factory used to create short-lived SquirrelBox contexts.</param>
    internal EntityFrameworkOutboxStore(ISquirrelBoxDbContextFactory<TDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkOutboxStore{TDbContext}"/> class.
    /// </summary>
    /// <param name="dbContextFactory">The DbContext factory used to create short-lived outbox contexts.</param>
    public EntityFrameworkOutboxStore(IDbContextFactory<TDbContext> dbContextFactory)
        : this(new SquirrelBoxDbContextFactoryAdapter<TDbContext>(dbContextFactory))
    {
    }

    /// <inheritdoc />
    public async ValueTask AddAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        await using var dbContext = _dbContextFactory.CreateDbContext();
        dbContext.Set<SquirrelBoxOutboxEnvelopeRecord>().Add(ToRecord(envelope));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<OutboxEnvelope> GetAsync(Ulid envelopeId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        return ToEnvelope(await FindByIdAsync(dbContext, envelopeId, cancellationToken));
    }

    /// <inheritdoc />
    public async ValueTask MarkPublishingAsync(
        Ulid envelopeId,
        DateTimeOffset publishingOnUtc,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        var record = await FindByIdAsync(dbContext, envelopeId, cancellationToken);
        record.Status = OutboxStatus.Publishing.ToString();
        record.PublishingOnUtc = publishingOnUtc;
        record.UpdatedOnUtc = publishingOnUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask MarkPublishedAsync(
        Ulid envelopeId,
        DateTimeOffset publishedOnUtc,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        var record = await FindByIdAsync(dbContext, envelopeId, cancellationToken);
        record.Status = OutboxStatus.Published.ToString();
        record.PublishedOnUtc = publishedOnUtc;
        record.UpdatedOnUtc = publishedOnUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask MarkFailedAsync(
        Ulid envelopeId,
        OutboxFailure failure,
        DateTimeOffset failedOnUtc,
        DateTimeOffset? nextAttemptOnUtc,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        var record = await FindByIdAsync(dbContext, envelopeId, cancellationToken);
        record.Status = OutboxStatus.Failed.ToString();
        record.Attempts++;
        record.FailureJson = Serialize(failure);
        record.NextAttemptOnUtc = nextAttemptOnUtc;
        record.UpdatedOnUtc = failedOnUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask MarkDiscardedAsync(
        Ulid envelopeId,
        DateTimeOffset discardedOnUtc,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        var record = await FindByIdAsync(dbContext, envelopeId, cancellationToken);
        record.Status = OutboxStatus.Discarded.ToString();
        record.UpdatedOnUtc = discardedOnUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<OutboxEnvelope>> QueryAsync(
        OutboxQuery query,
        CancellationToken cancellationToken = default)
    {
        query ??= new OutboxQuery();

        await using var dbContext = _dbContextFactory.CreateDbContext();
        var records = dbContext.Set<SquirrelBoxOutboxEnvelopeRecord>().AsNoTracking().AsQueryable();

        if (query.Status is { } status)
        {
            var statusName = status.ToString();
            records = records.Where(record => record.Status == statusName);
        }

        if (!string.IsNullOrWhiteSpace(query.Transport))
            records = records.Where(record => record.Transport == query.Transport);

        if (!string.IsNullOrWhiteSpace(query.Operation))
            records = records.Where(record => record.Operation == query.Operation);

        if (!string.IsNullOrWhiteSpace(query.CorrelationId))
            records = records.Where(record => record.CorrelationId == query.CorrelationId);

        var result = await records
            .OrderByDescending(record => record.CreatedOnUtc)
            .Take(Math.Max(1, query.Limit))
            .ToArrayAsync(cancellationToken);

        return result.Select(ToEnvelope).ToArray();
    }

    private static async Task<SquirrelBoxOutboxEnvelopeRecord> FindByIdAsync(
        TDbContext dbContext,
        Ulid envelopeId,
        CancellationToken cancellationToken)
        => await dbContext.Set<SquirrelBoxOutboxEnvelopeRecord>()
            .SingleOrDefaultAsync(record => record.Id == envelopeId.ToString(), cancellationToken)
            ?? throw new KeyNotFoundException($"Outbox envelope '{envelopeId}' was not found.");

    private static SquirrelBoxOutboxEnvelopeRecord ToRecord(OutboxEnvelope envelope)
        => new()
        {
            Id = envelope.Id.ToString(),
            Transport = envelope.Transport,
            Operation = envelope.Operation,
            Destination = envelope.Destination,
            PayloadType = envelope.PayloadType,
            Payload = envelope.Payload,
            ContentType = envelope.ContentType,
            CorrelationId = envelope.CorrelationId,
            TraceId = envelope.TraceId,
            Status = envelope.Status.ToString(),
            CreatedOnUtc = envelope.CreatedOnUtc,
            UpdatedOnUtc = envelope.UpdatedOnUtc,
            PublishingOnUtc = envelope.PublishingOnUtc,
            PublishedOnUtc = envelope.PublishedOnUtc,
            NextAttemptOnUtc = envelope.NextAttemptOnUtc,
            Attempts = envelope.Attempts,
            HeadersJson = Serialize(envelope.Headers),
            MetadataJson = Serialize(envelope.Metadata),
            FailureJson = Serialize(envelope.Failure)
        };

    private static OutboxEnvelope ToEnvelope(SquirrelBoxOutboxEnvelopeRecord record)
    {
        var envelope = new OutboxEnvelope
        {
            Id = Ulid.Parse(record.Id),
            Transport = record.Transport,
            Operation = record.Operation,
            Destination = record.Destination,
            PayloadType = record.PayloadType,
            Payload = record.Payload ?? [],
            ContentType = record.ContentType,
            CorrelationId = record.CorrelationId,
            TraceId = record.TraceId,
            Status = Enum.Parse<OutboxStatus>(record.Status),
            CreatedOnUtc = record.CreatedOnUtc,
            UpdatedOnUtc = record.UpdatedOnUtc,
            PublishingOnUtc = record.PublishingOnUtc,
            PublishedOnUtc = record.PublishedOnUtc,
            NextAttemptOnUtc = record.NextAttemptOnUtc,
            Attempts = record.Attempts,
            Failure = Deserialize<OutboxFailure>(record.FailureJson)
        };

        foreach (var item in Deserialize<Dictionary<string, string>>(record.HeadersJson) ?? new(StringComparer.OrdinalIgnoreCase))
            envelope.Headers[item.Key] = item.Value;

        foreach (var item in Deserialize<Dictionary<string, string>>(record.MetadataJson) ?? new(StringComparer.OrdinalIgnoreCase))
            envelope.Metadata[item.Key] = item.Value;

        return envelope;
    }

    private static string Serialize<T>(T value)
        => value is null ? null : JsonSerializer.Serialize(value, JsonOptions);

    private static T Deserialize<T>(string json)
        => string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);
}
