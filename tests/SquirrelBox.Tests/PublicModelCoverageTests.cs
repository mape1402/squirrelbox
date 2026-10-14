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
