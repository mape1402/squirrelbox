using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SquirrelBox.InMemory;

namespace SquirrelBox.Tests;

public sealed class CoreBranchCoverageTests
{
    [Theory]
    [InlineData(null, null, null, null, false)]
    [InlineData(null, "correlation", null, null, true)]
    [InlineData(null, null, "trace", null, true)]
    [InlineData(null, null, null, "attempt", true)]
    public void Metadata_has_values_checks_each_identity_component(
        string idempotencyKey,
        string correlationId,
        string traceId,
        string attemptId,
        bool expected)
    {
        var metadata = new SquirrelBoxMetadata
        {
            IdempotencyKey = idempotencyKey,
            CorrelationId = correlationId,
            TraceId = traceId,
            AttemptId = attemptId
        };

        Assert.Equal(expected, metadata.HasValues);
    }

    [Fact]
    public void Metadata_from_identity_handles_present_containers_with_missing_values()
    {
        var metadata = SquirrelBoxMetadata.FromIdentity(new SquirrelBoxIdentity
        {
            Operation = new SquirrelBoxOperationIdentity
            {
                CorrelationId = SquirrelBoxMetadataValue.Create("correlation-id", "correlation", SquirrelBoxMetadataValueSource.Generated)
            },
            Attempt = new SquirrelBoxAttemptIdentity
            {
                AttemptId = SquirrelBoxMetadataValue.Create("attempt-id", "attempt", SquirrelBoxMetadataValueSource.Generated)
            }
        });

        Assert.Null(metadata.IdempotencyKey);
        Assert.Equal("correlation", metadata.CorrelationId);
        Assert.Null(metadata.TraceId);
        Assert.Equal("attempt", metadata.AttemptId);
    }

    [Fact]
    public void Metadata_from_identity_handles_missing_operation_and_attempt_containers()
    {
        var metadata = SquirrelBoxMetadata.FromIdentity(new SquirrelBoxIdentity
        {
            Operation = null,
            Attempt = null
        });

        Assert.False(metadata.HasValues);
    }

    [Fact]
    public void Default_factories_cover_null_incoming_and_explicit_values()
    {
        var correlationFactory = new DefaultCorrelationIdFactory();
        var traceFactory = new DefaultTraceIdFactory();
        var explicitContext = new InboxIdentityFactoryContext
        {
            IncomingCorrelationId = "correlation",
            IncomingTraceId = "trace"
        };

        Assert.True(Ulid.TryParse(correlationFactory.Create(null), out _));
        Assert.Equal("correlation", correlationFactory.Create(explicitContext));
        Assert.True(Ulid.TryParse(traceFactory.Create(null), out _));
        Assert.Equal("trace", traceFactory.Create(explicitContext));
    }

    [Fact]
    public void Identity_accessor_covers_empty_prepared_and_assigned_states()
    {
        var holderField = typeof(AsyncLocalSquirrelBoxIdentityAccessor).GetField(
            "Holder",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(AsyncLocalSquirrelBoxIdentityAccessor), "Holder");
        var holder = holderField.GetValue(null);
        holder.GetType().GetProperty("Value")!.SetValue(holder, null);
        var accessor = new AsyncLocalSquirrelBoxIdentityAccessor();

        Assert.Null(accessor.Current);

        accessor.Prepare();
        var identity = new SquirrelBoxIdentity();
        accessor.Current = identity;

        Assert.Same(identity, accessor.Current);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("manual-owner")]
    public void Inbox_context_resolves_owner(string owner)
    {
        var context = new InboxContext(new InboxEntry(), owner, ownsCompletion: false);

        Assert.Equal(string.IsNullOrWhiteSpace(owner) ? "manual" : owner, context.Owner);
    }

    [Fact]
    public void Public_context_and_factory_guards_reject_null_dependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new InboxContext(null, "owner", true));
        Assert.Throws<ArgumentNullException>(() => new OutboxContext(null));
        Assert.Throws<ArgumentNullException>(() => new JsonInboxPayloadHasher(null));
        Assert.Throws<ArgumentNullException>(() => OutboxPublishResult.Failed(null));
        Assert.Throws<ArgumentNullException>(() => new DefaultInboxPolicyResolver(null));

        var fingerprinter = new PassthroughFingerprinter();
        Assert.Throws<ArgumentNullException>(() => new JsonInboxPayloadHasher(fingerprinter).ComputeHash(null));
    }

    [Fact]
    public void Internal_builder_rejects_null_services()
    {
        var builderType = typeof(ISquirrelBoxBuilder).Assembly.GetType("SquirrelBox.SquirrelBoxBuilder", throwOnError: true);
        var exception = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(builderType, new object[] { null }));

        Assert.IsType<ArgumentNullException>(exception.InnerException);
    }

    [Fact]
    public void Policy_resolver_rejects_null_result()
    {
        var resolver = new DefaultInboxPolicyResolver(Options.Create(new SquirrelBoxOptions()));

        Assert.Throws<ArgumentNullException>(() => resolver.Resolve(null));
    }

    [Fact]
    public void Payload_type_resolver_uses_name_when_full_name_is_unavailable()
    {
        var genericParameter = typeof(GenericPayload<>).GetGenericArguments()[0];

        var name = new DefaultOutboxPayloadTypeNameResolver().GetName(genericParameter);

        Assert.Equal("T", name);
    }

    [Fact]
    public void Default_outbox_profile_applies_explicit_values_and_copies_collections()
    {
        var serializer = new RecordingSerializer();
        var resolver = new RecordingTypeResolver();
        var now = DateTimeOffset.UtcNow;
        var request = new OutboxEnqueueRequest
        {
            Transport = "pigeon",
            Operation = "orders.created",
            ContentType = "application/custom",
            Payload = new Payload("one"),
            PayloadType = typeof(object),
            Destination = "orders"
        };
        request.Headers["header"] = "value";
        request.Metadata["metadata"] = "value";

        var envelope = new DefaultOutboxProfile().CreateEnvelope(
            request,
            new OutboxProfileContext(serializer, resolver, new OutboxOptions(), now));

        Assert.Equal("pigeon", envelope.Transport);
        Assert.Equal("orders.created", envelope.Operation);
        Assert.Equal("application/custom", envelope.ContentType);
        Assert.Equal("resolved:Object", envelope.PayloadType);
        Assert.Equal("value", envelope.Headers["header"]);
        Assert.Equal("value", envelope.Metadata["metadata"]);
        Assert.Equal(typeof(object), serializer.LastPayloadType);
    }

    [Fact]
    public void Default_outbox_profile_applies_all_fallback_values()
    {
        var serializer = new RecordingSerializer();
        var request = new OutboxEnqueueRequest
        {
            Transport = " ",
            Operation = " ",
            ContentType = " ",
            Payload = new Payload("one")
        };

        var envelope = new DefaultOutboxProfile().CreateEnvelope(
            request,
            new OutboxProfileContext(
                serializer,
                new RecordingTypeResolver(),
                new OutboxOptions { DefaultTransport = "default-transport" },
                DateTimeOffset.UtcNow));

        Assert.Equal("default-transport", envelope.Transport);
        Assert.Equal(typeof(Payload).FullName, envelope.Operation);
        Assert.Equal(serializer.ContentType, envelope.ContentType);
        Assert.Equal(typeof(Payload), serializer.LastPayloadType);
    }

    [Fact]
    public void Default_outbox_profile_uses_type_name_when_full_name_is_unavailable()
    {
        var genericParameter = typeof(GenericPayload<>).GetGenericArguments()[0];
        var request = new OutboxEnqueueRequest
        {
            Payload = new Payload("one"),
            PayloadType = genericParameter,
            Operation = " "
        };

        var envelope = new DefaultOutboxProfile().CreateEnvelope(
            request,
            new OutboxProfileContext(
                new RecordingSerializer(),
                new RecordingTypeResolver(),
                new OutboxOptions(),
                DateTimeOffset.UtcNow));

        Assert.Equal("T", envelope.Operation);
    }

    [Fact]
    public void Default_outbox_profile_rejects_null_inputs_and_payload()
    {
        var profile = new DefaultOutboxProfile();
        var context = new OutboxProfileContext(
            new RecordingSerializer(),
            new OutboxOptions(),
            DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentNullException>(() => profile.CreateEnvelope(null, context));
        Assert.Throws<ArgumentNullException>(() => profile.CreateEnvelope(new OutboxEnqueueRequest(), null));
        Assert.Throws<ArgumentNullException>(() => profile.CreateEnvelope(new OutboxEnqueueRequest(), context));
    }

    [Fact]
    public async Task Inbox_open_covers_null_metadata_explicit_payload_type_and_identity_names()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        using var activity = new Activity("core-branches").Start();

        var result = await inbox.OpenOrContinueAsync(new InboxOpenRequest
        {
            Source = "http",
            Operation = "POST /branches",
            IdempotencyKey = "key",
            PayloadType = "custom-payload",
            CorrelationId = "correlation",
            TraceId = "trace",
            IdempotencyKeyName = "custom-key",
            CorrelationIdName = "custom-correlation",
            TraceIdName = "custom-trace",
            AttemptIdName = "custom-attempt",
            Metadata = null,
            EntryLifetime = TimeSpan.Zero,
            CompletedLock = InboxCompletedLockMode.Forever,
            Deferred = new InboxDeferredPolicyOptions
            {
                Lane = "request-lane",
                MaxAttempts = 9,
                Delay = TimeSpan.FromSeconds(2),
                MaxDelay = TimeSpan.FromSeconds(3),
                Backoff = InboxRetryBackoff.Exponential,
                JitterRatio = 0.25,
                InProgressTimeout = TimeSpan.FromMinutes(2)
            }
        });

        Assert.Null(result.Entry.ExpiresOnUtc);
        Assert.Equal("custom-payload", result.Entry.PayloadType);
        Assert.Equal("correlation", result.Entry.CorrelationId);
        Assert.Equal(SquirrelBoxMetadataValueSource.Incoming, result.Entry.CorrelationIdSource);
        Assert.Equal(SquirrelBoxMetadataValueSource.Incoming, result.Entry.CurrentAttempt.TraceIdSource);
        Assert.Equal(InboxCompletedLockMode.Forever, result.Entry.CompletedLock);
        Assert.Equal("request-lane", result.Entry.Deferred.Lane);
        Assert.Equal(9, result.Entry.Deferred.MaxAttempts);
    }

    [Fact]
    public async Task Inbox_named_policy_covers_policy_fallbacks_and_generated_lane()
    {
        using var provider = CreateProvider(options =>
        {
            options.InboxPolicies["retry"] = new InboxIdempotencyPolicy
            {
                Name = "retry",
                AllowPayloadHashAsIdempotencyKey = true,
                EntryLifetime = TimeSpan.FromMinutes(3),
                CompletedLock = InboxCompletedLockMode.Default,
                ExecutionMode = InboxExecutionMode.Deferred
            };
            options.InboxPolicies["retry"].Deferred.MaxAttempts = 4;
            options.InboxPolicies["retry"].Deferred.Delay = TimeSpan.FromSeconds(1);
        });
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IInboxService>()
            .OpenOrContinueAsync(new InboxOpenRequest
            {
                Source = "http",
                Operation = "POST /policy",
                Payload = new Payload("policy"),
                PolicyName = "retry"
            });

        Assert.Equal(InboxExecutionMode.Deferred, result.Entry.ExecutionMode);
        Assert.Equal(InboxCompletedLockMode.UntilExpiration, result.Entry.CompletedLock);
        Assert.Equal(TimeSpan.FromMinutes(3), result.Entry.ExpiresOnUtc - result.Entry.CreatedOnUtc);
        Assert.Equal("squirrelbox.retry", result.Entry.Deferred.Lane);
        Assert.Equal(4, result.Entry.Deferred.MaxAttempts);
    }

    [Fact]
    public async Task Inbox_named_policy_uses_its_explicit_deferred_lane()
    {
        using var provider = CreateProvider(options =>
        {
            options.InboxPolicies["lane"] = new InboxIdempotencyPolicy { Name = "lane" };
            options.InboxPolicies["lane"].Deferred.Lane = "policy-lane";
        });
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IInboxService>()
            .OpenOrContinueAsync(new InboxOpenRequest
            {
                Source = "http",
                Operation = "POST /lane",
                IdempotencyKey = "lane-key",
                PolicyName = "lane"
            });

        Assert.Equal("policy-lane", result.Entry.Deferred.Lane);
    }

    [Fact]
    public async Task Inbox_uses_generated_trace_source_without_activity()
    {
        var previous = Activity.Current;
        Activity.Current = null;
        try
        {
            using var provider = CreateProvider();
            using var scope = provider.CreateScope();

            var result = await scope.ServiceProvider.GetRequiredService<IInboxService>()
                .OpenOrContinueAsync(new InboxOpenRequest
                {
                    Source = "http",
                    Operation = "POST /generated-trace",
                    IdempotencyKey = "generated-trace"
                });

            Assert.Equal(SquirrelBoxMetadataValueSource.Generated, result.Entry.CurrentAttempt.TraceIdSource);
        }
        finally
        {
            Activity.Current = previous;
        }
    }

    [Fact]
    public async Task Inbox_uses_activity_trace_and_accepts_explicit_key_without_payload()
    {
        using var activity = new Activity("core-activity-branch").Start();
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IInboxService>()
            .OpenOrContinueAsync(new InboxOpenRequest
            {
                Source = "http",
                Operation = "POST /activity",
                IdempotencyKey = "activity-key",
                Payload = null,
                PayloadType = null,
                ExpiresOnUtc = DateTimeOffset.UtcNow.AddMinutes(1)
            });

        Assert.Null(result.Entry.PayloadType);
        Assert.Equal(SquirrelBoxMetadataValueSource.Activity, result.Entry.CurrentAttempt.TraceIdSource);
    }

    [Fact]
    public async Task Inbox_event_uses_last_attempt_identity_when_current_attempt_is_missing()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var inbox = Assert.IsType<DefaultInbox>(scope.ServiceProvider.GetRequiredService<IInboxService>());
        var method = typeof(DefaultInbox).GetMethod(
            "PublishEventAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var entry = new InboxEntry
        {
            Id = Ulid.NewUlid(),
            Source = "http",
            Operation = "POST /events",
            IdempotencyKey = "event-key",
            CorrelationId = "event-correlation",
            LastAttemptId = "last-attempt",
            LastTraceId = "last-trace",
            CurrentAttempt = null
        };

        var pending = (ValueTask)method.Invoke(
            inbox,
            new object[] { SquirrelBoxEventNames.InboxOpened, entry, CancellationToken.None })!;
        await pending;

        var published = provider.GetRequiredService<ISquirrelBoxEventSink>().GetRecent().Last();
        Assert.Equal("last-attempt", published.Metadata["attempt-id"]);
        Assert.Equal("last-trace", published.Metadata["trace-id"]);
    }

    [Fact]
    public void Operation_registry_contract_resolution_handles_generic_and_invalid_root_types()
    {
        var method = typeof(DefaultSquirrelBoxOperationRegistry).GetMethod(
            "ResolveOperationContract",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var genericContract = method.Invoke(null, new object[] { typeof(SquirrelBoxOperation<,>) });
        var exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null, new object[] { typeof(object) }));

        Assert.NotNull(genericContract);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public void Operation_registry_contract_resolution_skips_unrelated_generic_contracts()
    {
        var method = typeof(DefaultSquirrelBoxOperationRegistry).GetMethod(
            "ResolveOperationContract",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var exception = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null, new object[] { typeof(GenericNonOperation) }));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public void Operation_registry_base_type_enumerator_reaches_the_root()
    {
        var method = typeof(DefaultSquirrelBoxOperationRegistry).GetMethod(
            "EnumerateBaseTypes",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        var hierarchy = ((IEnumerable<Type>)method.Invoke(null, new object[] { typeof(GenericBase<int>) })!).ToArray();

        Assert.Equal([typeof(GenericBase<int>), typeof(object)], hierarchy);
    }

    [Fact]
    public async Task Inbox_request_can_disable_payload_hash_fallback()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IInboxService>()
            .OpenOrContinueAsync(new InboxOpenRequest
            {
                Source = "http",
                Operation = "POST /disabled",
                Payload = new Payload("disabled"),
                AllowPayloadHashAsIdempotencyKey = false
            });

        Assert.Equal(InboxOpenState.MissingIdempotencyKey, result.State);
    }

    private static ServiceProvider CreateProvider(Action<SquirrelBoxOptions> configure = null)
    {
        var services = new ServiceCollection();
        services.AddSquirrelBox(configure).UseInMemory();
        return services.BuildServiceProvider();
    }

    private sealed record Payload(string Id);

    private sealed class GenericPayload<T>;

    private sealed class GenericNonOperation : IEnumerable<int>
    {
        public IEnumerator<int> GetEnumerator() => Enumerable.Empty<int>().GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class GenericBase<T>;

    private interface IBranchOutboxProfile : IOutboxProfile;

    private abstract class AbstractBranchOutboxProfile : IOutboxProfile
    {
        public abstract bool CanHandle(OutboxEnqueueRequest request);

        public abstract OutboxEnvelope CreateEnvelope(OutboxEnqueueRequest request, OutboxProfileContext context);
    }

    private sealed class PassthroughFingerprinter : IInboxPayloadFingerprinter
    {
        public object CreateFingerprint(object payload) => payload;
    }

    private sealed class RecordingSerializer : IOutboxEnvelopeSerializer
    {
        public string ContentType => "application/recording";

        public JsonSerializerOptions JsonOptions { get; } = new();

        public Type LastPayloadType { get; private set; }

        public byte[] Serialize(object payload, Type payloadType)
        {
            LastPayloadType = payloadType;
            return [1];
        }

        public object Deserialize(byte[] payload, Type payloadType) => new object();
    }

    private sealed class RecordingTypeResolver : IOutboxPayloadTypeNameResolver
    {
        public string GetName(Type payloadType) => $"resolved:{payloadType.Name}";

        public Type Resolve(string payloadTypeName) => typeof(object);
    }
}
