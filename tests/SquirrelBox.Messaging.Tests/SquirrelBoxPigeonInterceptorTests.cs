using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Mule;
using NSubstitute;
using Pigeon.Messaging.Consuming.Dispatching;
using Pigeon.Messaging.Contracts;
using SquirrelBox.InMemory;
using SquirrelBox.Messaging.Pigeon;
using SquirrelBox.Mule;

namespace SquirrelBox.Messaging.Tests;

public sealed class SquirrelBoxPigeonInterceptorTests
{
    [Fact]
    public async Task DecisionInterceptor_continues_without_opening_inbox_when_no_profile_matches()
    {
        using var provider = CreateProvider(scanProfiles: false);
        using var scope = provider.CreateScope();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(scope.ServiceProvider);

        var decision = await interceptor.InterceptAsync(context);

        Assert.Equal(PigeonConsumeDecision.Continue, decision.Decision);
        Assert.Null(scope.ServiceProvider.GetRequiredService<IInboxService>().Current);
        Assert.False(context.ReplyMetadata.ContainsKey(SquirrelBoxMetadataNames.MetadataSection));
    }

    [Fact]
    public async Task DecisionInterceptor_opens_inbox_and_continues_inline_consume()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(scope.ServiceProvider);

        var decision = await interceptor.InterceptAsync(context);

        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        Assert.Equal(PigeonConsumeDecision.Continue, decision.Decision);
        Assert.NotNull(inbox.Current);
        Assert.Equal("pigeon-key", inbox.Current.EffectiveIdempotencyKey);
        Assert.Equal("orders:1.2.0/billing/OrderMessage", inbox.Current.Entry.Operation);
        var replyMetadata = ReadSquirrelBoxMetadata(context.ReplyMetadata);
        var replyHeaders = ReadSquirrelBoxMetadata(context.ReplyHeaders);
        Assert.Equal("pigeon-key", replyMetadata.IdempotencyKey);
        Assert.Equal(inbox.Current.Entry.CorrelationId, replyMetadata.CorrelationId);
        Assert.Equal(inbox.Current.Entry.LastAttemptId, replyMetadata.AttemptId);
        Assert.Equal(inbox.Current.Entry.LastTraceId, replyMetadata.TraceId);
        Assert.Equal("pigeon-key", replyHeaders.IdempotencyKey);
        Assert.Equal(inbox.Current.Entry.CorrelationId, replyHeaders.CorrelationId);
        AssertNoFlatIdentityMetadata(context.ReplyMetadata);
        AssertNoFlatIdentityMetadata(context.ReplyHeaders);
    }

    [Fact]
    public async Task DecisionInterceptor_rejects_when_message_has_no_idempotency_material()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(
            scope.ServiceProvider,
            useDefaultMessage: false,
            message: null,
            messageType: null,
            operation: null,
            rawMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        var decision = await interceptor.InterceptAsync(context);

        Assert.NotEqual(PigeonConsumeDecision.Continue, decision.Decision);
        Assert.Contains(
            "MissingIdempotencyKey",
            decision.Reason,
            StringComparison.Ordinal);
        Assert.Empty(decision.Metadata);
    }

    [Fact]
    public async Task DecisionInterceptor_rejects_a_non_missing_key_duplicate_path()
    {
        using var provider = CreateProvider();
        using (var firstScope = provider.CreateScope())
        {
            var first = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(firstScope.ServiceProvider);
            await first.InterceptAsync(CreateContext(firstScope.ServiceProvider));
            await firstScope.ServiceProvider.GetRequiredService<IInboxService>().CompleteCurrentAsync();
        }

        using var secondScope = provider.CreateScope();
        var second = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(secondScope.ServiceProvider);

        var decision = await second.InterceptAsync(CreateContext(secondScope.ServiceProvider));

        Assert.Equal(PigeonConsumeDecision.AckAndSkip, decision.Decision);
        Assert.Contains("DuplicateCompleted", decision.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DecisionInterceptor_uses_custom_operation_version_and_message_id_resolvers()
    {
        using var provider = CreateProvider(options =>
        {
            options.MessageIdMetadataName = "custom-message-id";
            options.OperationResolver = _ => "resolved-operation";
            options.VersionResolver = _ => SemanticVersion.Parse("2.5.0");
        });
        using var scope = provider.CreateScope();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(
            scope.ServiceProvider,
            rawMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["custom-message-id"] = "message-from-metadata"
            });

        var decision = await interceptor.InterceptAsync(context);
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();

        Assert.Equal(PigeonConsumeDecision.Continue, decision.Decision);
        Assert.Equal("message-from-metadata", inbox.Current.EffectiveIdempotencyKey);
        Assert.Equal("orders:2.5.0/billing/resolved-operation", inbox.Current.Entry.Operation);
    }

    [Fact]
    public async Task DecisionInterceptor_uses_direct_message_id_and_context_operation_when_metadata_is_missing()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(
            scope.ServiceProvider,
            operation: "context-operation",
            rawMetadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            messageId: "direct-message-id",
            correlationId: "corr-direct");

        var decision = await interceptor.InterceptAsync(context);
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();

        Assert.Equal(PigeonConsumeDecision.Continue, decision.Decision);
        Assert.Equal("direct-message-id", inbox.Current.EffectiveIdempotencyKey);
        Assert.Equal("orders:1.2.0/billing/context-operation", inbox.Current.Entry.Operation);
        Assert.Equal("corr-direct", inbox.Current.Entry.CorrelationId);
    }

    [Fact]
    public async Task DecisionInterceptor_uses_message_type_or_message_fallback_operation_and_null_raw_metadata()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var typedContext = CreateContext(
            scope.ServiceProvider,
            messageId: "typed-message-id",
            useNullRawMetadata: true);
        var fallbackContext = CreateContext(
            scope.ServiceProvider,
            useDefaultMessage: false,
            message: null,
            messageType: null,
            operation: null,
            messageId: "fallback-message-id",
            useNullRawMetadata: true);

        await interceptor.InterceptAsync(typedContext);
        var typedOperation = scope.ServiceProvider.GetRequiredService<IInboxService>().Current.Entry.Operation;
        scope.ServiceProvider.GetRequiredService<IInboxService>().ReleaseCurrent();

        await interceptor.InterceptAsync(fallbackContext);
        var fallbackOperation = scope.ServiceProvider.GetRequiredService<IInboxService>().Current.Entry.Operation;

        Assert.Equal("orders:1.2.0/billing/OrderMessage", typedOperation);
        Assert.Equal("orders:1.2.0/billing/message", fallbackOperation);
    }

    [Fact]
    public async Task DecisionInterceptor_opens_when_broker_message_id_metadata_is_not_configured()
    {
        using var provider = CreateProvider(options => options.MessageIdMetadataName = " ");
        using var scope = provider.CreateScope();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var metadata = CreateRawMetadata("pigeon-key");
        metadata.Remove("message-id");
        var context = CreateContext(scope.ServiceProvider, rawMetadata: metadata);

        var decision = await interceptor.InterceptAsync(context);

        Assert.Equal(PigeonConsumeDecision.Continue, decision.Decision);
        Assert.Equal("pigeon-key", scope.ServiceProvider.GetRequiredService<IInboxService>().Current.EffectiveIdempotencyKey);
    }

    [Theory]
    [InlineData(InboxPolicyAction.Skip, PigeonConsumeDecision.AckAndSkip)]
    [InlineData(InboxPolicyAction.Replay, PigeonConsumeDecision.AckAndSkip)]
    [InlineData(InboxPolicyAction.Retry, PigeonConsumeDecision.Retry)]
    [InlineData(InboxPolicyAction.Reopen, PigeonConsumeDecision.Retry)]
    [InlineData(InboxPolicyAction.Reject, PigeonConsumeDecision.Reject)]
    [InlineData(InboxPolicyAction.Fail, PigeonConsumeDecision.Reject)]
    public void DecisionInterceptor_maps_neutral_decisions_to_pigeon_decisions(
        InboxPolicyAction action,
        PigeonConsumeDecision expected)
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var method = typeof(SquirrelBoxPigeonDecisionInterceptor).GetMethod("MapDecision", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(nameof(SquirrelBoxPigeonDecisionInterceptor), "MapDecision");

        var result = (PigeonConsumeDecisionResult)method.Invoke(
            interceptor,
            [new InboxDecision(InboxOpenState.DuplicateInProgress, action, new InboxEntry())]);

        Assert.Equal(expected, result.Decision);
        Assert.Contains(action.ToString(), result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void DecisionInterceptor_maps_unknown_neutral_decision_to_rejected_decision()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var method = typeof(SquirrelBoxPigeonDecisionInterceptor).GetMethod("MapDecision", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(nameof(SquirrelBoxPigeonDecisionInterceptor), "MapDecision");

        var result = (PigeonConsumeDecisionResult)method.Invoke(
            interceptor,
            [new InboxDecision(InboxOpenState.DuplicateInProgress, (InboxPolicyAction)999, new InboxEntry())]);

        Assert.Equal(PigeonConsumeDecision.Reject, result.Decision);
    }

    [Fact]
    public async Task ExecutionInterceptor_completes_inbox_after_successful_handler()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var decision = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var execution = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonExecutionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(scope.ServiceProvider);

        await decision.InterceptAsync(context);
        var entryId = scope.ServiceProvider.GetRequiredService<IInboxService>().Current.Entry.Id;

        await execution.InvokeAsync(context, (_, _) => ValueTask.CompletedTask);

        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        var entry = await inbox.GetAsync(entryId);

        Assert.Equal(InboxStatus.Completed, entry.Status);
        Assert.Null(inbox.Current);
    }

    [Fact]
    public async Task ExecutionInterceptor_fails_inbox_when_handler_throws()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var decision = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var execution = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonExecutionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(scope.ServiceProvider);

        await decision.InterceptAsync(context);
        var entryId = scope.ServiceProvider.GetRequiredService<IInboxService>().Current.Entry.Id;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            execution.InvokeAsync(
                context,
                (_, _) => throw new InvalidOperationException("boom")).AsTask());

        var entry = await scope.ServiceProvider.GetRequiredService<IInboxService>().GetAsync(entryId);
        Assert.Equal(InboxStatus.Failed, entry.Status);
        Assert.Equal("boom", entry.FailureDetails.ErrorMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecutionInterceptor_does_not_finalize_a_replaced_context(bool handlerFails)
    {
        var inbox = Substitute.For<IInboxService>();
        var first = new InboxContext(new InboxEntry(), "first", ownsCompletion: true);
        var replacement = new InboxContext(new InboxEntry(), "replacement", ownsCompletion: true);
        inbox.Current.Returns(first, replacement);
        var execution = new SquirrelBoxPigeonExecutionInterceptor(inbox);
        var context = new ConsumeContext();

        if (handlerFails)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => execution.InvokeAsync(
                context,
                (_, _) => throw new InvalidOperationException("boom")).AsTask());
        }
        else
        {
            await execution.InvokeAsync(context, (_, _) => ValueTask.CompletedTask);
        }

        await inbox.DidNotReceive().CompleteCurrentAsync(
            Arg.Any<InboxCompletion>(),
            Arg.Any<CancellationToken>());
        await inbox.DidNotReceive().FailCurrentAsync(
            Arg.Any<Exception>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExecutionInterceptor_skips_missing_or_non_owning_contexts(
        bool hasContext,
        bool handlerFails)
    {
        var inbox = Substitute.For<IInboxService>();
        inbox.Current.Returns(hasContext
            ? new InboxContext(new InboxEntry(), "observer", ownsCompletion: false)
            : null);
        var execution = new SquirrelBoxPigeonExecutionInterceptor(inbox);
        var context = new ConsumeContext();

        if (handlerFails)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => execution.InvokeAsync(
                context,
                (_, _) => throw new InvalidOperationException("boom")).AsTask());
        }
        else
        {
            await execution.InvokeAsync(context, (_, _) => ValueTask.CompletedTask);
        }

        await inbox.DidNotReceive().CompleteCurrentAsync(
            Arg.Any<InboxCompletion>(),
            Arg.Any<CancellationToken>());
        await inbox.DidNotReceive().FailCurrentAsync(
            Arg.Any<Exception>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DecisionInterceptor_defers_consume_and_schedules_pigeon_envelope()
    {
        using var provider = CreateProvider(options => options.ExecutionModeResolver = _ => InboxExecutionMode.Deferred);
        using var scope = provider.CreateScope();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(scope.ServiceProvider);

        var result = await interceptor.InterceptAsync(context);

        var scheduler = scope.ServiceProvider.GetRequiredService<FakeInboxMuleScheduler>();

        Assert.Equal(PigeonConsumeDecision.Defer, result.Decision);
        Assert.Single(scheduler.Payloads);
        var envelope = Assert.IsType<PigeonConsumeEnvelope>(scheduler.Payloads.Single());
        Assert.Equal("orders", envelope.Topic);
        var envelopeMetadata = ReadSquirrelBoxMetadata(envelope.Metadata);
        var resultMetadata = ReadSquirrelBoxMetadata(result.Metadata);
        Assert.Equal("pigeon-key", envelopeMetadata.IdempotencyKey);
        Assert.False(string.IsNullOrWhiteSpace(envelopeMetadata.CorrelationId));
        Assert.False(string.IsNullOrWhiteSpace(envelopeMetadata.AttemptId));
        Assert.False(string.IsNullOrWhiteSpace(envelopeMetadata.TraceId));
        Assert.Equal(envelopeMetadata.CorrelationId, resultMetadata.CorrelationId);
        Assert.Equal(envelopeMetadata.AttemptId, resultMetadata.AttemptId);
        Assert.Equal(envelopeMetadata.TraceId, resultMetadata.TraceId);
        AssertNoFlatIdentityMetadata(envelope.Metadata);
        AssertNoFlatIdentityMetadata(result.Metadata);
        Assert.Null(scope.ServiceProvider.GetRequiredService<IInboxService>().Current);
    }

    [Fact]
    public async Task DecisionInterceptor_marks_current_inbox_failed_when_deferred_scheduling_fails()
    {
        using var provider = CreateProvider(options => options.ExecutionModeResolver = _ => InboxExecutionMode.Deferred);
        using var scope = provider.CreateScope();
        var scheduler = scope.ServiceProvider.GetRequiredService<FakeInboxMuleScheduler>();
        scheduler.ExceptionToThrow = new InvalidOperationException("scheduler failed");
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(scope.ServiceProvider);

        await Assert.ThrowsAsync<InvalidOperationException>(() => interceptor.InterceptAsync(context).AsTask());

        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        Assert.Equal(InboxStatus.Failed, inbox.LastContext.Entry.Status);
        Assert.Equal("scheduler failed", inbox.LastContext.Entry.FailureDetails.ErrorMessage);
    }

    [Fact]
    public async Task DecisionInterceptor_normalizes_empty_pigeon_version_for_deferred_envelope()
    {
        using var provider = CreateProvider(options => options.ExecutionModeResolver = _ => InboxExecutionMode.Deferred);
        using var scope = provider.CreateScope();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(scope.ServiceProvider, version: new SemanticVersion(0, 0, 0));

        await interceptor.InterceptAsync(context);

        var scheduler = scope.ServiceProvider.GetRequiredService<FakeInboxMuleScheduler>();
        var envelope = Assert.IsType<PigeonConsumeEnvelope>(scheduler.Payloads.Single());

        Assert.Equal(SemanticVersion.Default, envelope.Version);
        Assert.Equal("orders:1.0.0/billing/OrderMessage", scope.ServiceProvider.GetRequiredService<IInboxService>().LastContext.Entry.Operation);
    }

    [Fact]
    public async Task DecisionInterceptor_continues_without_opening_new_inbox_during_deferred_replay()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        await inbox.OpenOrContinueAsync(InboxOpenRequest.For("pigeon", "orders", "pigeon-key", new OrderMessage("order-1")));

        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(scope.ServiceProvider, ConsumeExecutionSource.DeferredReplay);

        var result = await interceptor.InterceptAsync(context);

        Assert.Equal(PigeonConsumeDecision.Continue, result.Decision);
        Assert.Empty(scope.ServiceProvider.GetRequiredService<FakeInboxMuleScheduler>().Payloads);
        Assert.Equal("pigeon-key", inbox.Current.EffectiveIdempotencyKey);
        var replyMetadata = ReadSquirrelBoxMetadata(context.ReplyMetadata);
        Assert.Equal("pigeon-key", replyMetadata.IdempotencyKey);
        Assert.Equal(inbox.Current.Entry.CorrelationId, replyMetadata.CorrelationId);
        Assert.Equal(inbox.Current.Entry.LastAttemptId, replyMetadata.AttemptId);
        Assert.Equal(inbox.Current.Entry.LastTraceId, replyMetadata.TraceId);
        AssertNoFlatIdentityMetadata(context.ReplyMetadata);
    }

    [Fact]
    public async Task DecisionInterceptor_uses_last_context_metadata_during_deferred_replay_when_current_is_released()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        await inbox.OpenOrContinueAsync(InboxOpenRequest.For("pigeon", "orders", "pigeon-key", new OrderMessage("order-1")));
        inbox.ReleaseCurrent();

        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(scope.ServiceProvider, ConsumeExecutionSource.DeferredReplay);

        var result = await interceptor.InterceptAsync(context);

        Assert.Equal(PigeonConsumeDecision.Continue, result.Decision);
        var replyMetadata = ReadSquirrelBoxMetadata(context.ReplyMetadata);
        Assert.Equal("pigeon-key", replyMetadata.IdempotencyKey);
    }

    [Fact]
    public async Task DecisionInterceptor_deferred_replay_without_an_inbox_context_writes_no_metadata()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var context = CreateContext(scope.ServiceProvider, ConsumeExecutionSource.DeferredReplay);

        var result = await interceptor.InterceptAsync(context);

        Assert.Equal(PigeonConsumeDecision.Continue, result.Decision);
        Assert.Empty(context.ReplyMetadata);
        Assert.Empty(context.ReplyHeaders);
    }

    [Fact]
    public async Task DecisionInterceptor_maps_decision_with_last_context_metadata()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        await inbox.OpenOrContinueAsync(InboxOpenRequest.For("pigeon", "orders", "last-key", new OrderMessage("one")));
        inbox.ReleaseCurrent();
        var interceptor = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonDecisionInterceptor>(scope.ServiceProvider);
        var method = typeof(SquirrelBoxPigeonDecisionInterceptor).GetMethod(
            "MapDecision",
            BindingFlags.NonPublic | BindingFlags.Instance)!;

        var result = (PigeonConsumeDecisionResult)method.Invoke(
            interceptor,
            [new InboxDecision(InboxOpenState.DuplicateCompleted, InboxPolicyAction.Replay, inbox.LastContext.Entry)])!;

        Assert.Equal("last-key", ReadSquirrelBoxMetadata(result.Metadata).IdempotencyKey);
    }

    [Fact]
    public void DecisionInterceptor_with_version_creates_empty_header_and_metadata_dictionaries()
    {
        var method = typeof(SquirrelBoxPigeonDecisionInterceptor).GetMethod(
            "WithVersion",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(SquirrelBoxPigeonDecisionInterceptor), "WithVersion");
        var envelope = new PigeonConsumeEnvelope
        {
            Topic = "orders",
            Headers = null,
            Metadata = null
        };

        var cloned = (PigeonConsumeEnvelope)method.Invoke(null, [envelope, SemanticVersion.Parse("3.0.0")]);

        Assert.Equal(SemanticVersion.Parse("3.0.0"), cloned.Version);
        Assert.Empty(cloned.Headers);
        Assert.Empty(cloned.Metadata);
        Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [null, SemanticVersion.Default]));
    }

    [Fact]
    public async Task ExecutionInterceptor_does_not_complete_inbox_during_deferred_replay()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        var opened = await inbox.OpenOrContinueAsync(InboxOpenRequest.For("pigeon", "orders", "pigeon-key", new OrderMessage("order-1")));
        var context = CreateContext(scope.ServiceProvider, ConsumeExecutionSource.DeferredReplay);
        var execution = ActivatorUtilities.CreateInstance<SquirrelBoxPigeonExecutionInterceptor>(scope.ServiceProvider);

        await execution.InvokeAsync(context, (_, _) => ValueTask.CompletedTask);

        var entry = await inbox.GetAsync(opened.Entry.Id);
        Assert.Equal(InboxStatus.Started, entry.Status);
        Assert.NotNull(inbox.Current);
    }

    [Fact]
    public async Task PigeonMuleAction_continues_inbox_invokes_consumer_and_completes_entry()
    {
        using var provider = CreateProvider();
        Ulid entryId;

        using (var openScope = provider.CreateScope())
        {
            var inbox = openScope.ServiceProvider.GetRequiredService<IInboxService>();
            var opened = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
                source: "pigeon",
                operation: "orders",
                idempotencyKey: "pigeon-key",
                payload: new OrderMessage("order-1"),
                executionMode: InboxExecutionMode.Deferred));

            entryId = opened.Entry.Id;
            inbox.ReleaseCurrent();
        }

        using (var workerScope = provider.CreateScope())
        {
            var invoker = workerScope.ServiceProvider.GetRequiredService<FakePigeonConsumerInvoker>();
            var action = new SquirrelBoxPigeonMuleAction(
                workerScope.ServiceProvider.GetRequiredService<IInboxService>(),
                invoker);

            await action.ExecuteAsync(
                CreateMuleContext(workerScope.ServiceProvider, entryId),
                CancellationToken.None);
        }

        using var verifyScope = provider.CreateScope();
        var entry = await verifyScope.ServiceProvider.GetRequiredService<IInboxService>().GetAsync(entryId);
        var fakeInvoker = provider.GetRequiredService<FakePigeonConsumerInvoker>();

        Assert.Equal(InboxStatus.Completed, entry.Status);
        Assert.Single(fakeInvoker.Envelopes);
        Assert.Equal("orders", fakeInvoker.Envelopes.Single().Topic);
    }

    [Fact]
    public async Task PigeonMuleAction_restores_serialized_version_from_envelope_metadata()
    {
        using var provider = CreateProvider();
        Ulid entryId;

        using (var openScope = provider.CreateScope())
        {
            var inbox = openScope.ServiceProvider.GetRequiredService<IInboxService>();
            var opened = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
                source: "pigeon",
                operation: "orders",
                idempotencyKey: "pigeon-key",
                payload: new OrderMessage("order-1"),
                executionMode: InboxExecutionMode.Deferred));

            entryId = opened.Entry.Id;
            inbox.ReleaseCurrent();
        }

        using (var workerScope = provider.CreateScope())
        {
            var invoker = workerScope.ServiceProvider.GetRequiredService<FakePigeonConsumerInvoker>();
            var action = new SquirrelBoxPigeonMuleAction(
                workerScope.ServiceProvider.GetRequiredService<IInboxService>(),
                invoker);

            await action.ExecuteAsync(
                CreateMuleContext(
                    workerScope.ServiceProvider,
                    entryId,
                    payloadVersion: new SemanticVersion(0, 0, 0),
                    metadataVersion: "1.2.3"),
                CancellationToken.None);
        }

        var fakeInvoker = provider.GetRequiredService<FakePigeonConsumerInvoker>();

        Assert.Equal(SemanticVersion.Parse("1.2.3"), fakeInvoker.Envelopes.Single().Version);
    }

    private static ServiceProvider CreateProvider(Action<SquirrelBoxPigeonOptions> configure = null, bool scanProfiles = true)
    {
        var services = new ServiceCollection();
        services.AddSquirrelBox().UseInMemory();
        services.AddSquirrelBoxMessaging(options =>
        {
            if (scanProfiles)
                options.ScanAssemblyContaining<SquirrelBoxPigeonInterceptorTests>();
        });
        services.AddSingleton<FakeInboxMuleScheduler>();
        services.AddSingleton<IInboxMuleScheduler>(provider => provider.GetRequiredService<FakeInboxMuleScheduler>());
        services.AddSingleton<FakePigeonConsumerInvoker>();
        services.AddSingleton<IPigeonConsumerInvoker>(provider => provider.GetRequiredService<FakePigeonConsumerInvoker>());
        services.AddSingleton<IPigeonConsumeEnvelopeFactory, FakePigeonConsumeEnvelopeFactory>();
        services.AddSingleton(Options.Create(CreateOptions(configure)));
        return services.BuildServiceProvider();
    }

    private static SquirrelBoxPigeonOptions CreateOptions(Action<SquirrelBoxPigeonOptions> configure)
    {
        var options = new SquirrelBoxPigeonOptions();
        configure?.Invoke(options);
        return options;
    }

    private static ConsumeContext CreateContext(
        IServiceProvider services,
        ConsumeExecutionSource executionSource = ConsumeExecutionSource.BrokerDelivery,
        SemanticVersion? version = null,
        object message = null,
        Type messageType = null,
        string operation = null,
        IReadOnlyDictionary<string, string> rawMetadata = null,
        bool useDefaultMessage = true,
        string messageId = null,
        string correlationId = null,
        bool useNullRawMetadata = false)
        => new()
        {
            Services = services,
            Topic = "orders",
            Subscription = "billing",
            MessageVersion = version ?? SemanticVersion.Parse("1.2.0"),
            Message = useDefaultMessage ? message ?? new OrderMessage("order-1") : message,
            MessageType = useDefaultMessage ? messageType ?? typeof(OrderMessage) : messageType,
            Operation = operation,
            MessageId = messageId,
            CorrelationId = correlationId,
            ExecutionSource = executionSource,
            RawMetadata = useNullRawMetadata ? null : rawMetadata ?? CreateRawMetadata("pigeon-key")
        };

    private sealed record OrderMessage(string Id);

    private static Dictionary<string, string> CreateRawMetadata(string idempotencyKey)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["message-id"] = "broker-id"
        };

        new SquirrelBoxMessageMetadata
        {
            Metadata = new SquirrelBoxMetadata
            {
                IdempotencyKey = idempotencyKey
            }
        }.WriteTo(metadata);

        return metadata;
    }

    private static SquirrelBoxMetadata ReadSquirrelBoxMetadata(IEnumerable<KeyValuePair<string, string>> metadata)
    {
        Assert.True(SquirrelBoxMessageMetadata.TryReadFrom(metadata, out var squirrelBoxMetadata));
        return squirrelBoxMetadata.Metadata;
    }

    private static void AssertNoFlatIdentityMetadata(IEnumerable<KeyValuePair<string, string>> metadata)
    {
        Assert.DoesNotContain(metadata, item => IsFlatIdentityKey(item.Key));
    }

    private static bool IsFlatIdentityKey(string key)
    {
        return string.Equals(key, SquirrelBoxMetadataNames.IdempotencyKey, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, SquirrelBoxMetadataNames.CorrelationId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, SquirrelBoxMetadataNames.AttemptId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, SquirrelBoxMetadataNames.TraceId, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakePigeonConsumeEnvelopeFactory : IPigeonConsumeEnvelopeFactory
    {
        public PigeonConsumeEnvelope Create(ConsumeContext context)
            => new()
            {
                Topic = context.Topic,
                Version = context.MessageVersion,
                Subscription = context.Subscription,
                PayloadType = context.MessageType.AssemblyQualifiedName,
                Payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(context.Message, context.MessageType),
                CreatedOnUtc = DateTimeOffset.UtcNow,
                Metadata = new Dictionary<string, string>(context.RawMetadata, StringComparer.OrdinalIgnoreCase)
            };

        public ConsumeContext CreateContext(
            PigeonConsumeEnvelope envelope,
            IServiceProvider services,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeInboxMuleScheduler : IInboxMuleScheduler
    {
        public List<object> Payloads { get; } = [];

        public Exception ExceptionToThrow { get; set; }

        public ValueTask<Guid> EnqueueCurrentAsync<TPayload>(
            ActionKey key,
            TPayload payload,
            Action<EnqueueOptions> configure = null,
            CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            Payloads.Add(payload);
            configure?.Invoke(new EnqueueOptions());
            return ValueTask.FromResult(Guid.NewGuid());
        }
    }

    private static MuleActionContext<PigeonConsumeEnvelope> CreateMuleContext(
        IServiceProvider services,
        Ulid entryId,
        SemanticVersion? payloadVersion = null,
        string metadataVersion = null)
    {
        var durableAction = new DurableAction
        {
            Key = SquirrelBoxPigeonMuleActionKeys.ConsumeKey,
            Metadata = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
            {
                [SquirrelBoxMuleMetadata.InboxEntryId] = entryId.ToString(),
                [SquirrelBoxMuleMetadata.IdempotencyKey] = "pigeon-key"
            })
        };

        var envelope = new PigeonConsumeEnvelope
        {
            Topic = "orders",
            Version = payloadVersion ?? SemanticVersion.Default,
            Subscription = "billing",
            PayloadType = typeof(OrderMessage).AssemblyQualifiedName,
            Payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new OrderMessage("order-1")),
            CreatedOnUtc = DateTimeOffset.UtcNow,
            Metadata = metadataVersion is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["squirrelbox-pigeon-version"] = metadataVersion
                }
        };

        return new MuleActionContext<PigeonConsumeEnvelope>(durableAction, services, envelope);
    }

    private sealed class FakePigeonConsumerInvoker : IPigeonConsumerInvoker
    {
        public List<PigeonConsumeEnvelope> Envelopes { get; } = [];

        public ValueTask InvokeAsync(
            PigeonConsumeEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            return ValueTask.CompletedTask;
        }
    }
}

public sealed class SquirrelBoxPigeonTestPolicyProfile : InboxMessagePolicyProfile
{
    public override void Configure(InboxMessagePolicyProfileBuilder builder)
        => builder.ForTopic("orders");
}
