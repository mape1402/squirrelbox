using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SquirrelBox.EntityFrameworkCore;

namespace SquirrelBox.EntityFrameworkCore.Tests;

public sealed class EntityFrameworkMappingBranchTests
{
    private static readonly Type InboxStoreType = typeof(EntityFrameworkInboxStore<MappingBranchDbContext>);
    private static readonly Type OutboxStoreType = typeof(EntityFrameworkOutboxStore<MappingBranchDbContext>);

    [Fact]
    public void Stores_require_internal_db_context_factory()
    {
        Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(
            InboxStoreType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [null],
            culture: null));
        Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(
            OutboxStoreType,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [null],
            culture: null));
    }

    [Fact]
    public void Inbox_mapping_handles_null_deferred_completion_failure_and_metadata()
    {
        var entry = new InboxEntry
        {
            Id = Ulid.NewUlid(),
            Source = "http",
            Operation = "POST /orders",
            IdempotencyKey = "key",
            Deferred = null,
            Completion = null,
            FailureDetails = null,
            Metadata = null
        };

        var record = Invoke<SquirrelBoxInboxEntryRecord>(InboxStoreType, "ToRecord", entry);
        var mapped = Invoke<InboxEntry>(InboxStoreType, "ToEntry", new SquirrelBoxInboxEntryRecord
        {
            Id = Ulid.NewUlid(),
            Source = "http",
            Operation = "POST /orders",
            IdempotencyKey = "key",
            IdempotencyKeySource = InboxIdempotencyKeySource.ComputedFromPayload.ToString(),
            CorrelationIdSource = null,
            Status = InboxStatus.Started.ToString(),
            ExecutionMode = InboxExecutionMode.Inline.ToString(),
            CompletedLock = null,
            MetadataJson = null
        });

        Assert.Null(record.DeferredPolicyName);
        Assert.Null(record.DeferredDelayMilliseconds);
        Assert.Equal(SquirrelBoxMetadataValueSource.Missing, mapped.CorrelationIdSource);
        Assert.Equal(InboxCompletedLockMode.UntilExpiration, mapped.CompletedLock);
        Assert.Empty(mapped.Metadata);
    }

    [Fact]
    public void Inbox_mapping_handles_full_deferred_completion_failure_and_metadata()
    {
        var entry = new InboxEntry
        {
            Id = Ulid.NewUlid(),
            Source = "http",
            Operation = "POST /orders",
            IdempotencyKey = "key",
            IdempotencyKeySource = InboxIdempotencyKeySource.Explicit,
            CorrelationIdSource = SquirrelBoxMetadataValueSource.Incoming,
            Status = InboxStatus.Started,
            ExecutionMode = InboxExecutionMode.Deferred,
            CompletedLock = InboxCompletedLockMode.Forever,
            Deferred = new InboxDeferredExecutionOptions
            {
                PolicyName = "policy",
                Lane = "orders",
                MaxAttempts = 3,
                Delay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(5),
                Backoff = InboxRetryBackoff.Linear,
                JitterRatio = 0.1,
                InProgressTimeout = TimeSpan.FromMinutes(1)
            },
            Completion = new InboxCompletion { StatusCode = 202 },
            FailureDetails = new InboxFailure { Details = "failed" },
            Metadata = new Dictionary<string, string> { ["one"] = "two" }
        };

        var record = Invoke<SquirrelBoxInboxEntryRecord>(InboxStoreType, "ToRecord", entry);
        var mapped = Invoke<InboxEntry>(InboxStoreType, "ToEntry", record);

        Assert.Equal("policy", record.DeferredPolicyName);
        Assert.Equal(1000, record.DeferredDelayMilliseconds);
        Assert.Equal(InboxRetryBackoff.Linear.ToString(), record.DeferredBackoff);
        Assert.Equal(InboxCompletedLockMode.Forever, mapped.CompletedLock);
        Assert.Equal(InboxRetryBackoff.Linear, mapped.Deferred.Backoff);
        Assert.Equal("two", mapped.Metadata["one"]);
        Assert.Equal(202, mapped.Completion.StatusCode);
        Assert.Equal("failed", mapped.FailureDetails.Details);
    }

    [Theory]
    [InlineData(InboxStatus.Failed, InboxOpenState.DuplicateFailed)]
    [InlineData(InboxStatus.Expired, InboxOpenState.Expired)]
    public void Inbox_duplicate_state_maps_terminal_statuses(
        InboxStatus status,
        InboxOpenState expected)
    {
        var state = Invoke<InboxOpenState>(
            InboxStoreType,
            "MapDuplicateState",
            new InboxEntry { Status = status });

        Assert.Equal(expected, state);
    }

    [Theory]
    [InlineData(InboxStatus.Completed, InboxOpenState.DuplicateCompleted)]
    [InlineData(InboxStatus.Started, InboxOpenState.DuplicateInProgress)]
    public void Inbox_duplicate_state_maps_completed_and_active_statuses(
        InboxStatus status,
        InboxOpenState expected)
    {
        var state = Invoke<InboxOpenState>(
            InboxStoreType,
            "MapDuplicateState",
            new InboxEntry { Status = status });

        Assert.Equal(expected, state);
    }

    [Fact]
    public void Inbox_active_timeout_requires_active_positive_timeout_and_elapsed_window()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.False(Invoke<bool>(
            InboxStoreType,
            "IsActiveTimedOut",
            new InboxEntry { Status = InboxStatus.Completed },
            now));
        Assert.False(Invoke<bool>(
            InboxStoreType,
            "IsActiveTimedOut",
            new InboxEntry { Status = InboxStatus.Failed },
            now));
        Assert.False(Invoke<bool>(
            InboxStoreType,
            "IsActiveTimedOut",
            new InboxEntry { Status = InboxStatus.Expired },
            now));
        Assert.False(Invoke<bool>(
            InboxStoreType,
            "IsActiveTimedOut",
            new InboxEntry { Status = InboxStatus.Started, Deferred = null },
            now));
        Assert.False(Invoke<bool>(
            InboxStoreType,
            "IsActiveTimedOut",
            new InboxEntry
            {
                Status = InboxStatus.Started,
                UpdatedOnUtc = now,
                Deferred = new InboxDeferredExecutionOptions { InProgressTimeout = TimeSpan.Zero }
            },
            now.AddMinutes(1)));
        Assert.False(Invoke<bool>(
            InboxStoreType,
            "IsActiveTimedOut",
            new InboxEntry
            {
                Status = InboxStatus.Started,
                UpdatedOnUtc = now,
                Deferred = new InboxDeferredExecutionOptions { InProgressTimeout = null }
            },
            now.AddMinutes(1)));
        Assert.True(Invoke<bool>(
            InboxStoreType,
            "IsActiveTimedOut",
            new InboxEntry
            {
                Status = InboxStatus.Retrying,
                UpdatedOnUtc = now,
                Deferred = new InboxDeferredExecutionOptions { InProgressTimeout = TimeSpan.FromSeconds(1) }
            },
            now.AddSeconds(2)));
        Assert.True(Invoke<bool>(
            InboxStoreType,
            "IsActiveTimedOut",
            new InboxEntry
            {
                Status = InboxStatus.Started,
                UpdatedOnUtc = now,
                Deferred = new InboxDeferredExecutionOptions { InProgressTimeout = TimeSpan.FromSeconds(1) }
            },
            now.AddSeconds(2)));
    }

    [Fact]
    public void Inbox_mapping_covers_attempt_identity_lock_and_optional_backoff_branches()
    {
        var attemptId = Ulid.NewUlid();
        var cloned = Invoke<InboxAttempt>(
            InboxStoreType,
            "CloneAttempt",
            new InboxAttempt
            {
                Id = attemptId,
                AttemptId = "attempt",
                Metadata = new Dictionary<string, string>()
            },
            Ulid.NewUlid(),
            InboxOpenState.Opened);
        var generatedIdentity = Invoke<InboxAttempt>(
            InboxStoreType,
            "CloneAttempt",
            new InboxAttempt
            {
                Id = default,
                AttemptId = "generated-attempt",
                Metadata = new Dictionary<string, string>()
            },
            Ulid.NewUlid(),
            InboxOpenState.Opened);
        var record = new SquirrelBoxInboxEntryRecord();
        var mappedRecord = Invoke<SquirrelBoxInboxEntryRecord>(InboxStoreType, "ToRecord", new InboxEntry
        {
            Id = Ulid.NewUlid(),
            Source = "http",
            Operation = "POST /branch",
            IdempotencyKey = "key",
            Deferred = new InboxDeferredExecutionOptions { Backoff = null }
        });

        Invoke<object>(
            InboxStoreType,
            "ApplyDeferred",
            record,
            new InboxDeferredExecutionOptions { Backoff = null });
        Invoke<object>(
            InboxStoreType,
            "ApplyDeferred",
            record,
            new InboxDeferredExecutionOptions { Backoff = InboxRetryBackoff.Exponential });

        Assert.Equal(attemptId, cloned.Id);
        Assert.NotEqual(default, generatedIdentity.Id);
        Assert.Null(mappedRecord.DeferredBackoff);
        Assert.Equal(
            InboxCompletedLockMode.UntilExpiration,
            Invoke<InboxCompletedLockMode>(InboxStoreType, "ResolveCompletedLock", InboxCompletedLockMode.Default));
        Assert.Equal(
            InboxCompletedLockMode.Forever,
            Invoke<InboxCompletedLockMode>(InboxStoreType, "ResolveCompletedLock", InboxCompletedLockMode.Forever));
        Assert.Equal(InboxRetryBackoff.Exponential.ToString(), record.DeferredBackoff);
    }

    [Fact]
    public void Inbox_reopen_handles_missing_attempt_and_preserves_or_replaces_correlation()
    {
        var existing = new SquirrelBoxInboxEntryRecord
        {
            Id = Ulid.NewUlid(),
            Source = "http",
            Operation = "POST /orders",
            IdempotencyKey = "key",
            IdempotencyKeySource = InboxIdempotencyKeySource.Explicit.ToString(),
            CorrelationId = "existing-correlation",
            CorrelationIdName = "Correlation-Id",
            CorrelationIdSource = SquirrelBoxMetadataValueSource.Generated.ToString(),
            Status = InboxStatus.Failed.ToString(),
            ExecutionMode = InboxExecutionMode.Inline.ToString(),
            CompletedLock = InboxCompletedLockMode.Default.ToString(),
            CreatedOnUtc = DateTimeOffset.UtcNow,
            UpdatedOnUtc = DateTimeOffset.UtcNow
        };
        var incoming = new InboxEntry
        {
            Source = "http",
            Operation = "POST /orders",
            IdempotencyKey = "key",
            IdempotencyKeySource = InboxIdempotencyKeySource.ComputedFromPayload,
            CorrelationId = "incoming-correlation",
            CorrelationIdName = "Incoming-Correlation",
            CorrelationIdSource = SquirrelBoxMetadataValueSource.Incoming,
            ExecutionMode = InboxExecutionMode.Deferred,
            CompletedLock = InboxCompletedLockMode.Forever,
            Deferred = null,
            Metadata = new Dictionary<string, string>()
        };

        Invoke<object>(InboxStoreType, "Reopen", existing, incoming, true);
        Assert.Equal("existing-correlation", existing.CorrelationId);
        Assert.Null(existing.OriginalAttemptId);

        incoming.CurrentAttempt = new InboxAttempt { AttemptId = "attempt", TraceId = "trace" };
        Invoke<object>(InboxStoreType, "Reopen", existing, incoming, false);

        Assert.Equal("incoming-correlation", existing.CorrelationId);
        Assert.Equal("attempt", existing.OriginalAttemptId);
        Assert.Equal("trace", existing.OriginalTraceId);
        Assert.Equal(InboxCompletedLockMode.Forever.ToString(), existing.CompletedLock);
        Assert.Null(existing.DeferredBackoff);
    }

    [Fact]
    public void Inbox_apply_deferred_accepts_null_deferred_options()
    {
        var record = new SquirrelBoxInboxEntryRecord();

        Invoke<object>(InboxStoreType, "ApplyDeferred", record, null);

        Assert.Null(record.DeferredLane);
        Assert.Null(record.DeferredDelayMilliseconds);
    }

    [Fact]
    public void Outbox_mapping_handles_null_payload_headers_metadata_and_failure()
    {
        var record = new SquirrelBoxOutboxEnvelopeRecord
        {
            Id = Ulid.NewUlid(),
            Transport = "pigeon",
            Operation = "orders.created",
            Destination = "orders",
            PayloadType = typeof(string).FullName,
            Payload = null,
            ContentType = "application/json",
            Status = OutboxStatus.Pending.ToString(),
            CreatedOnUtc = DateTimeOffset.UtcNow,
            UpdatedOnUtc = DateTimeOffset.UtcNow,
            HeadersJson = null,
            MetadataJson = null,
            FailureJson = null
        };

        var envelope = Invoke<OutboxEnvelope>(OutboxStoreType, "ToEnvelope", record);
        var attempt = Invoke<SquirrelBoxOutboxAttemptRecord>(
            OutboxStoreType,
            "CreateAttemptRecord",
            record,
            DateTimeOffset.UtcNow);

        Assert.Empty(envelope.Payload);
        Assert.Empty(envelope.Headers);
        Assert.Empty(envelope.Metadata);
        Assert.Null(envelope.Failure);
        Assert.Contains("pigeon", attempt.MetadataJson, StringComparison.Ordinal);

        var mappedAttempt = Invoke<OutboxAttempt>(OutboxStoreType, "ToAttempt", new SquirrelBoxOutboxAttemptRecord
        {
            Id = Ulid.NewUlid(),
            OutboxEnvelopeId = record.Id,
            AttemptId = "attempt",
            State = OutboxAttemptState.Publishing.ToString(),
            StartedOnUtc = DateTimeOffset.UtcNow,
            FailureJson = null,
            MetadataJson = null
        });
        Assert.Null(mappedAttempt.Failure);
        Assert.Empty(mappedAttempt.Metadata);
    }

    [Fact]
    public void Outbox_mapping_handles_full_payload_headers_metadata_failure_and_attempts()
    {
        var envelope = new OutboxEnvelope
        {
            Id = Ulid.NewUlid(),
            Transport = "pigeon",
            Operation = "orders.created",
            Destination = "orders",
            PayloadType = typeof(string).FullName,
            Payload = [1, 2, 3],
            ContentType = "application/json",
            CorrelationId = "corr",
            TraceId = "trace",
            Status = OutboxStatus.Failed,
            Failure = new OutboxFailure { Details = "failed" },
            Attempts = 2,
            CreatedOnUtc = DateTimeOffset.UtcNow,
            UpdatedOnUtc = DateTimeOffset.UtcNow
        };
        envelope.Headers["h"] = "v";
        envelope.Metadata["m"] = "v";

        var record = Invoke<SquirrelBoxOutboxEnvelopeRecord>(OutboxStoreType, "ToRecord", envelope);
        var mapped = Invoke<OutboxEnvelope>(OutboxStoreType, "ToEnvelope", record);
        var attempt = Invoke<OutboxAttempt>(OutboxStoreType, "ToAttempt", new SquirrelBoxOutboxAttemptRecord
        {
            Id = Ulid.NewUlid(),
            OutboxEnvelopeId = envelope.Id,
            AttemptId = "attempt",
            TraceId = "trace",
            State = OutboxAttemptState.Failed.ToString(),
            StartedOnUtc = DateTimeOffset.UtcNow,
            CompletedOnUtc = DateTimeOffset.UtcNow,
            FailureJson = record.FailureJson,
            MetadataJson = record.MetadataJson
        });

        Assert.Equal([1, 2, 3], mapped.Payload);
        Assert.Equal("v", mapped.Headers["h"]);
        Assert.Equal("v", mapped.Metadata["m"]);
        Assert.Equal("failed", mapped.Failure.Details);
        Assert.Equal(OutboxAttemptState.Failed, attempt.State);
        Assert.Equal("failed", attempt.Failure.Details);
        Assert.Equal("v", attempt.Metadata["m"]);
    }

    private static T Invoke<T>(Type type, string methodName, params object[] parameters)
    {
        var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(type.FullName, methodName);
        return (T)method.Invoke(null, parameters);
    }

    private sealed class MappingBranchDbContext : DbContext
    {
        public MappingBranchDbContext(DbContextOptions<MappingBranchDbContext> options)
            : base(options)
        {
        }
    }
}
