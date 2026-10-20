using System.Diagnostics;
using System.Reflection;

namespace SquirrelBox.Tests;

public sealed class PublicModelCoverageTests
{
    [Fact]
    public void Outbox_profile_context_short_constructor_uses_default_payload_type_resolver()
    {
        var now = DateTimeOffset.UtcNow;
        var serializer = new JsonOutboxEnvelopeSerializer();

        var context = new OutboxProfileContext(serializer, new OutboxOptions(), now);

        Assert.Same(serializer, context.Serializer);
        Assert.IsType<DefaultOutboxPayloadTypeNameResolver>(context.PayloadTypes);
        Assert.Equal(now, context.CreatedOnUtc);
    }

    [Fact]
    public void Deferred_policy_reports_configuration_from_lane_or_timeout()
    {
        var property = typeof(InboxDeferredPolicyOptions).GetProperty(
            "HasConfiguration",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMemberException(nameof(InboxDeferredPolicyOptions), "HasConfiguration");

        Assert.True((bool)property.GetValue(new InboxDeferredPolicyOptions { Lane = "orders" }));
        Assert.True((bool)property.GetValue(new InboxDeferredPolicyOptions { InProgressTimeout = TimeSpan.FromSeconds(1) }));
    }

    [Fact]
    public void Trace_factory_uses_current_activity_when_no_incoming_trace_exists()
    {
        using var activity = new Activity("squirrelbox-test").Start();

        var traceId = new DefaultTraceIdFactory().Create(new InboxIdentityFactoryContext());

        Assert.Equal(activity.TraceId.ToString(), traceId);
    }

    [Fact]
    public void Serializer_exposes_json_options_and_round_trips_payload()
    {
        var serializer = new JsonOutboxEnvelopeSerializer();

        var payload = serializer.Serialize(new Payload("one"), typeof(Payload));
        var restored = Assert.IsType<Payload>(serializer.Deserialize(payload, typeof(Payload)));

        Assert.NotNull(serializer.JsonOptions);
        Assert.Equal("one", restored.Id);
    }

    [Fact]
    public void Operation_base_exposes_request_and_result_types()
    {
        var operation = new TestOperation();

        Assert.Equal(typeof(Payload), operation.RequestType);
        Assert.Equal(typeof(string), operation.ResultType);
    }

    [Fact]
    public void Operation_result_exposes_effective_key_from_decision()
    {
        var result = new SquirrelBoxOperationResult<string>(
            SquirrelBoxOperationExecutionState.Skipped,
            result: null,
            new InboxDecision(
                InboxOpenState.DuplicateCompleted,
                InboxPolicyAction.Replay,
                new InboxEntry { IdempotencyKey = "key-from-decision" }),
            context: null);

        Assert.Equal("key-from-decision", result.EffectiveIdempotencyKey);
        Assert.False(result.Executed);
        Assert.False(result.Deferred);
    }

    [Fact]
    public void Metadata_from_identity_returns_empty_for_null_identity()
    {
        Assert.Same(SquirrelBoxMetadata.Empty, SquirrelBoxMetadata.FromIdentity(null));
    }

    [Fact]
    public void Metadata_from_identity_handles_partial_identity_shapes()
    {
        var operationOnly = SquirrelBoxMetadata.FromIdentity(new SquirrelBoxIdentity
        {
            Operation = new SquirrelBoxOperationIdentity
            {
                IdempotencyKey = SquirrelBoxMetadataValue.Create(null, "key", SquirrelBoxMetadataValueSource.Generated)
            }
        });
        var attemptOnly = SquirrelBoxMetadata.FromIdentity(new SquirrelBoxIdentity
        {
            Attempt = new SquirrelBoxAttemptIdentity
            {
                TraceId = SquirrelBoxMetadataValue.Create(null, "trace", SquirrelBoxMetadataValueSource.Generated)
            }
        });
        var empty = SquirrelBoxMetadata.FromIdentity(new SquirrelBoxIdentity());

        Assert.Equal("key", operationOnly.IdempotencyKey);
        Assert.Null(operationOnly.CorrelationId);
        Assert.Equal("trace", attemptOnly.TraceId);
        Assert.Null(attemptOnly.AttemptId);
        Assert.False(empty.HasValues);
    }

    [Fact]
    public void Metadata_from_identity_reads_all_identity_values()
    {
        var metadata = SquirrelBoxMetadata.FromIdentity(new SquirrelBoxIdentity
        {
            Operation = new SquirrelBoxOperationIdentity
            {
                IdempotencyKey = SquirrelBoxMetadataValue.Create("Idempotency-Key", "key", SquirrelBoxMetadataValueSource.Incoming),
                CorrelationId = SquirrelBoxMetadataValue.Create("Correlation-Id", "corr", SquirrelBoxMetadataValueSource.Generated)
            },
            Attempt = new SquirrelBoxAttemptIdentity
            {
                AttemptId = SquirrelBoxMetadataValue.Create("Attempt-Id", "attempt", SquirrelBoxMetadataValueSource.Generated),
                TraceId = SquirrelBoxMetadataValue.Create("Trace-Id", "trace", SquirrelBoxMetadataValueSource.Activity)
            }
        });

        Assert.True(metadata.HasValues);
        Assert.Equal("key", metadata.IdempotencyKey);
        Assert.Equal("corr", metadata.CorrelationId);
        Assert.Equal("attempt", metadata.AttemptId);
        Assert.Equal("trace", metadata.TraceId);
    }

    [Fact]
    public void Inbox_entry_identity_uses_current_then_last_attempt_values()
    {
        var current = new InboxEntry
        {
            IdempotencyKey = "key-current",
            IdempotencyKeyName = "Idempotency-Key",
            IdempotencyKeySource = InboxIdempotencyKeySource.ComputedFromPayload,
            CorrelationId = "corr-current",
            CorrelationIdName = "Correlation-Id",
            CorrelationIdSource = SquirrelBoxMetadataValueSource.Generated,
            LastAttemptId = "last-attempt",
            LastTraceId = "last-trace",
            CurrentAttempt = new InboxAttempt
            {
                AttemptId = "current-attempt",
                AttemptIdName = "Attempt-Id",
                AttemptIdSource = SquirrelBoxMetadataValueSource.Incoming,
                TraceId = "current-trace",
                TraceIdName = "Trace-Id",
                TraceIdSource = SquirrelBoxMetadataValueSource.Activity
            }
        };
        var fallback = new InboxEntry
        {
            IdempotencyKey = "key-last",
            CorrelationId = "corr-last",
            LastAttemptId = "last-attempt",
            LastTraceId = "last-trace"
        };

        var currentIdentity = current.ToIdentity();
        var fallbackIdentity = fallback.ToIdentity();

        Assert.Equal("current-attempt", currentIdentity.Attempt.AttemptId.Value);
        Assert.Equal("current-trace", currentIdentity.Attempt.TraceId.Value);
        Assert.Equal(SquirrelBoxMetadataValueSource.ComputedFromPayload, currentIdentity.Operation.IdempotencyKey.Source);
        Assert.Equal("last-attempt", fallbackIdentity.Attempt.AttemptId.Value);
        Assert.Equal("last-trace", fallbackIdentity.Attempt.TraceId.Value);
        Assert.Equal(SquirrelBoxMetadataValueSource.Generated, fallbackIdentity.Attempt.AttemptId.Source);
    }

    [Fact]
    public void Inbox_open_result_exposes_effective_values_from_entry_attempts()
    {
        var entryWithLastAttempt = new InboxEntry
        {
            IdempotencyKey = "key",
            CorrelationId = "corr",
            LastAttemptId = "last-attempt",
            LastTraceId = "last-trace"
        };
        var entryWithCurrentAttempt = new InboxEntry
        {
            IdempotencyKey = "key",
            CorrelationId = "corr",
            LastAttemptId = "last-attempt",
            LastTraceId = "last-trace",
            CurrentAttempt = new InboxAttempt
            {
                AttemptId = "current-attempt",
                TraceId = "current-trace"
            }
        };
        var last = new InboxOpenResult(InboxOpenState.DuplicateCompleted, entry: entryWithLastAttempt);
        var current = new InboxOpenResult(InboxOpenState.Opened, entry: entryWithCurrentAttempt);

        Assert.Equal("last-attempt", last.EffectiveAttemptId);
        Assert.Equal("last-trace", last.EffectiveTraceId);
        Assert.Equal("current-attempt", current.EffectiveAttemptId);
        Assert.Equal("current-trace", current.EffectiveTraceId);
        Assert.NotNull(current.Identity);
    }

    [Fact]
    public void Inbox_open_result_returns_null_attempt_values_without_entry_or_attempts()
    {
        var missing = new InboxOpenResult(InboxOpenState.MissingIdempotencyKey);
        var withoutAttempts = new InboxOpenResult(
            InboxOpenState.Opened,
            entry: new InboxEntry { IdempotencyKey = "key" });

        Assert.Null(missing.EffectiveAttemptId);
        Assert.Null(missing.EffectiveTraceId);
        Assert.Null(missing.EffectiveIdempotencyKey);
        Assert.Null(missing.EffectiveCorrelationId);
        Assert.Null(missing.Identity);
        Assert.Null(withoutAttempts.EffectiveAttemptId);
        Assert.Null(withoutAttempts.EffectiveTraceId);
    }

    [Fact]
    public void Public_result_states_cover_all_success_and_failure_values()
    {
        Assert.True(new InboxOpenResult(InboxOpenState.Opened).Accepted);
        Assert.True(new InboxOpenResult(InboxOpenState.Continued).Accepted);
        Assert.False(new InboxOpenResult(InboxOpenState.DuplicateCompleted).Accepted);

        Assert.True(new InboxPayloadVerificationResult(InboxPayloadVerificationState.Verified).Success);
        Assert.True(new InboxPayloadVerificationResult(InboxPayloadVerificationState.Attached).Success);
        Assert.False(new InboxPayloadVerificationResult(InboxPayloadVerificationState.PayloadConflict).Success);

        var failure = new OutboxFailure { Details = "failed" };
        var failed = OutboxPublishResult.Failed(failure);
        Assert.False(failed.Succeeded);
        Assert.Same(failure, failed.Failure);
    }

    [Fact]
    public void Inbox_open_result_exposes_nullable_key_source_only_when_entry_exists()
    {
        var missing = new InboxOpenResult(InboxOpenState.MissingIdempotencyKey);
        var present = new InboxOpenResult(
            InboxOpenState.Opened,
            entry: new InboxEntry { IdempotencyKeySource = InboxIdempotencyKeySource.Explicit });

        Assert.Null(missing.IdempotencyKeySource);
        Assert.Equal(InboxIdempotencyKeySource.Explicit, present.IdempotencyKeySource);
    }

    [Fact]
    public void Inbox_open_result_prefers_context_entry_when_entry_argument_is_missing()
    {
        var context = new InboxContext(
            new InboxEntry
            {
                IdempotencyKey = "context-key",
                CurrentAttempt = new InboxAttempt
                {
                    AttemptId = "context-attempt",
                    TraceId = "context-trace"
                }
            },
            previous: null,
            owner: "test",
            ownsCompletion: true);

        var result = new InboxOpenResult(InboxOpenState.Opened, context);

        Assert.Equal("context-key", result.EffectiveIdempotencyKey);
        Assert.Equal("context-attempt", result.EffectiveAttemptId);
        Assert.Equal("context-trace", result.EffectiveTraceId);
    }

    [Fact]
    public void Operation_result_uses_context_key_when_decision_has_no_entry()
    {
        var entry = new InboxEntry { IdempotencyKey = "key-from-context" };
        var context = new InboxContext(entry, previous: null, owner: "test", ownsCompletion: true);
        var result = new SquirrelBoxOperationResult<string>(
            SquirrelBoxOperationExecutionState.Deferred,
            result: null,
            new InboxDecision(InboxOpenState.Opened, InboxPolicyAction.Continue, null),
            context);

        Assert.Equal("key-from-context", result.EffectiveIdempotencyKey);
        Assert.Equal(InboxIdempotencyKeySource.Explicit, context.IdempotencyKeySource);
        Assert.False(result.Executed);
        Assert.True(result.Deferred);
    }

    [Fact]
    public void Inbox_decision_record_members_are_available_to_transport_adapters()
    {
        var entry = new InboxEntry { IdempotencyKey = "decision-key" };
        var decision = new InboxDecision(InboxOpenState.Opened, InboxPolicyAction.Continue, entry);

        Assert.Equal(
            new InboxDecision(InboxOpenState.Opened, InboxPolicyAction.Continue, entry),
            decision);
        Assert.Contains(nameof(InboxDecision), decision.ToString(), StringComparison.Ordinal);
        Assert.Equal(decision, decision with { });
        var (state, action, relatedEntry) = decision;
        Assert.Equal(InboxOpenState.Opened, state);
        Assert.Equal(InboxPolicyAction.Continue, action);
        Assert.Same(entry, relatedEntry);
    }

    [Fact]
    public void Operation_result_returns_null_key_without_decision_or_context()
    {
        var result = new SquirrelBoxOperationResult<string>(
            SquirrelBoxOperationExecutionState.Executed,
            result: "ok",
            decision: null,
            context: null);

        Assert.True(result.Executed);
        Assert.False(result.Deferred);
        Assert.Equal("ok", result.Result);
        Assert.Null(result.EffectiveIdempotencyKey);
    }

    private sealed record Payload(string Id);

    private sealed class TestOperation : SquirrelBoxOperation<Payload, string>
    {
        protected override ValueTask<string> ExecuteAsync(
            Payload request,
            SquirrelBoxOperationContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(request.Id);
    }
}
