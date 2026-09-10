using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace SquirrelBox.EntityFrameworkCore;

/// <summary>
/// Entity Framework Core implementation of <see cref="IInboxStore"/>.
/// </summary>
/// <typeparam name="TDbContext">The DbContext type used to persist inbox entries.</typeparam>
public sealed class EntityFrameworkInboxStore<TDbContext> : IInboxStore, IInboxDiagnosticsStore
    where TDbContext : DbContext
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISquirrelBoxDbContextFactory<TDbContext> _dbContextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkInboxStore{TDbContext}"/> class.
    /// </summary>
    /// <param name="dbContextFactory">The DbContext factory used to create short-lived SquirrelBox contexts.</param>
    internal EntityFrameworkInboxStore(ISquirrelBoxDbContextFactory<TDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory ?? throw new ArgumentNullException(nameof(dbContextFactory));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkInboxStore{TDbContext}"/> class.
    /// </summary>
    /// <param name="dbContextFactory">The DbContext factory used to create short-lived inbox contexts.</param>
    public EntityFrameworkInboxStore(IDbContextFactory<TDbContext> dbContextFactory)
        : this(new SquirrelBoxDbContextFactoryAdapter<TDbContext>(dbContextFactory))
    {
    }

    /// <inheritdoc />
    public async ValueTask<InboxOpenResult> TryOpenAsync(InboxEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var dbContext = _dbContextFactory.CreateDbContext();
        var record = ToRecord(entry);
        dbContext.Set<SquirrelBoxInboxEntryRecord>().Add(record);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new InboxOpenResult(InboxOpenState.Opened, entry: entry);
        }
        catch (DbUpdateException)
        {
            dbContext.Entry(record).State = EntityState.Detached;
            var existing = await FindByBusinessKeyAsync(dbContext, entry, cancellationToken);
            return ResolveDuplicate(existing, entry);
        }
    }

    /// <inheritdoc />
    public async ValueTask<InboxPayloadVerificationResult> AttachPayloadHashAsync(
        Ulid entryId,
        string payloadHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadHash);

        await using var dbContext = _dbContextFactory.CreateDbContext();
        var record = await FindByIdAsync(dbContext, entryId, cancellationToken);

        if (string.IsNullOrWhiteSpace(record.PayloadHash))
        {
            record.PayloadHash = payloadHash;
            await dbContext.SaveChangesAsync(cancellationToken);
            return new InboxPayloadVerificationResult(InboxPayloadVerificationState.Attached, ToEntry(record));
        }

        var entry = ToEntry(record);
        return string.Equals(record.PayloadHash, payloadHash, StringComparison.Ordinal)
            ? new InboxPayloadVerificationResult(InboxPayloadVerificationState.Verified, entry)
            : new InboxPayloadVerificationResult(InboxPayloadVerificationState.PayloadConflict, entry);
    }

    /// <inheritdoc />
    public async ValueTask MarkCompletedAsync(
        Ulid entryId,
        InboxCompletion completion,
        DateTimeOffset completedOnUtc,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        var record = await FindByIdAsync(dbContext, entryId, cancellationToken);
        record.Status = InboxStatus.Completed.ToString();
        record.CompletionJson = Serialize(completion ?? InboxCompletion.Empty);
        record.CompletedOnUtc = completedOnUtc;
        record.UpdatedOnUtc = completedOnUtc;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask MarkFailedAsync(
        Ulid entryId,
        InboxFailure failure,
        DateTimeOffset failedOnUtc,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        var record = await FindByIdAsync(dbContext, entryId, cancellationToken);

        record.Status = InboxStatus.Failed.ToString();
        record.Failure = failure?.Details;
        record.FailureDetailsJson = Serialize(failure);
        record.UpdatedOnUtc = failedOnUtc;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<InboxEntry> GetAsync(Ulid entryId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = _dbContextFactory.CreateDbContext();
        return ToEntry(await FindByIdAsync(dbContext, entryId, cancellationToken));
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<InboxEntry>> QueryAsync(
        InboxQuery query,
        CancellationToken cancellationToken = default)
    {
        query ??= new InboxQuery();

        await using var dbContext = _dbContextFactory.CreateDbContext();
        var records = dbContext.Set<SquirrelBoxInboxEntryRecord>().AsNoTracking().AsQueryable();

        if (query.Status is { } status)
        {
            var statusName = status.ToString();
            records = records.Where(record => record.Status == statusName);
        }

        if (!string.IsNullOrWhiteSpace(query.Source))
            records = records.Where(record => record.Source == query.Source);

        if (!string.IsNullOrWhiteSpace(query.Operation))
            records = records.Where(record => record.Operation == query.Operation);

        if (!string.IsNullOrWhiteSpace(query.IdempotencyKey))
            records = records.Where(record => record.IdempotencyKey == query.IdempotencyKey);

        if (!string.IsNullOrWhiteSpace(query.CorrelationId))
            records = records.Where(record => record.CorrelationId == query.CorrelationId);

        var result = await records
            .OrderByDescending(record => record.CreatedOnUtc)
            .Take(Math.Max(1, query.Limit))
            .ToArrayAsync(cancellationToken);

        return result.Select(ToEntry).ToArray();
    }

    private static async Task<SquirrelBoxInboxEntryRecord> FindByBusinessKeyAsync(
        TDbContext dbContext,
        InboxEntry entry,
        CancellationToken cancellationToken)
        => await dbContext.Set<SquirrelBoxInboxEntryRecord>()
            .AsNoTracking()
            .SingleAsync(
                record => record.Source == entry.Source &&
                          record.Operation == entry.Operation &&
                          record.IdempotencyKey == entry.IdempotencyKey,
                cancellationToken);

    private static async Task<SquirrelBoxInboxEntryRecord> FindByIdAsync(
        TDbContext dbContext,
        Ulid entryId,
        CancellationToken cancellationToken)
        => await dbContext.Set<SquirrelBoxInboxEntryRecord>()
            .SingleOrDefaultAsync(record => record.Id == entryId.ToString(), cancellationToken)
            ?? throw new InboxEntryNotFoundException(entryId);

    private static InboxOpenResult ResolveDuplicate(SquirrelBoxInboxEntryRecord existing, InboxEntry incoming)
    {
        var entry = ToEntry(existing);

        if (entry.ExpiresOnUtc is { } expiresOnUtc && expiresOnUtc <= incoming.CreatedOnUtc)
            return new InboxOpenResult(InboxOpenState.Expired, entry: entry);

        if (!string.IsNullOrWhiteSpace(entry.PayloadHash) &&
            !string.IsNullOrWhiteSpace(incoming.PayloadHash) &&
            !string.Equals(entry.PayloadHash, incoming.PayloadHash, StringComparison.Ordinal))
        {
            return new InboxOpenResult(InboxOpenState.PayloadConflict, entry: entry);
        }

        return new InboxOpenResult(MapDuplicateState(entry), entry: entry);
    }

    private static InboxOpenState MapDuplicateState(InboxEntry entry)
        => entry.Status switch
        {
            InboxStatus.Completed => InboxOpenState.DuplicateCompleted,
            InboxStatus.Failed => InboxOpenState.DuplicateFailed,
            InboxStatus.Expired => InboxOpenState.Expired,
            _ => InboxOpenState.DuplicateInProgress
        };

    private static SquirrelBoxInboxEntryRecord ToRecord(InboxEntry entry)
        => new()
        {
            Id = entry.Id.ToString(),
            Source = entry.Source,
            Operation = entry.Operation,
            IdempotencyKey = entry.IdempotencyKey,
            IdempotencyKeySource = entry.IdempotencyKeySource.ToString(),
            PayloadHash = entry.PayloadHash,
            PayloadType = entry.PayloadType,
            CorrelationId = entry.CorrelationId,
            Status = entry.Status.ToString(),
            ExecutionMode = entry.ExecutionMode.ToString(),
            CreatedOnUtc = entry.CreatedOnUtc,
            UpdatedOnUtc = entry.UpdatedOnUtc,
            CompletedOnUtc = entry.CompletedOnUtc,
            ExpiresOnUtc = entry.ExpiresOnUtc,
            Failure = entry.Failure,
            CompletionJson = Serialize(entry.Completion),
            FailureDetailsJson = Serialize(entry.FailureDetails),
            MetadataJson = Serialize(entry.Metadata)
        };

    private static InboxEntry ToEntry(SquirrelBoxInboxEntryRecord record)
        => new()
        {
            Id = Ulid.Parse(record.Id),
            Source = record.Source,
            Operation = record.Operation,
            IdempotencyKey = record.IdempotencyKey,
            IdempotencyKeySource = Enum.Parse<InboxIdempotencyKeySource>(record.IdempotencyKeySource),
            PayloadHash = record.PayloadHash,
            PayloadType = record.PayloadType,
            CorrelationId = record.CorrelationId,
            Status = Enum.Parse<InboxStatus>(record.Status),
            ExecutionMode = Enum.Parse<InboxExecutionMode>(record.ExecutionMode),
            CreatedOnUtc = record.CreatedOnUtc,
            UpdatedOnUtc = record.UpdatedOnUtc,
            CompletedOnUtc = record.CompletedOnUtc,
            ExpiresOnUtc = record.ExpiresOnUtc,
            Failure = record.Failure,
            Completion = Deserialize<InboxCompletion>(record.CompletionJson),
            FailureDetails = Deserialize<InboxFailure>(record.FailureDetailsJson),
            Metadata = Deserialize<Dictionary<string, string>>(record.MetadataJson) ?? new(StringComparer.OrdinalIgnoreCase)
        };

    private static string Serialize<T>(T value)
        => value is null ? null : JsonSerializer.Serialize(value, JsonOptions);

    private static T Deserialize<T>(string json)
        => string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, JsonOptions);
}
