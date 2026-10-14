using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SquirrelBox.InMemory;

namespace SquirrelBox.Tests;

public sealed class OperationServiceBranchCoverageTests
{
    [Fact]
    public void Non_executing_result_maps_every_policy_action_and_null_completion()
    {
        var create = typeof(DefaultSquirrelBoxOperationService).GetMethod(
            "CreateNonExecutingResult",
            BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(string));

        var replayed = InvokeResult(create, new InboxDecision(
            InboxOpenState.DuplicateCompleted,
            InboxPolicyAction.Replay,
            null));
        var rejected = InvokeResult(create, new InboxDecision(
            InboxOpenState.DuplicateInProgress,
            InboxPolicyAction.Reject,
            new InboxEntry()));
        var failed = InvokeResult(create, new InboxDecision(
            InboxOpenState.DuplicateFailed,
            InboxPolicyAction.Fail,
            new InboxEntry()));
        var skipped = InvokeResult(create, new InboxDecision(
            InboxOpenState.Opened,
            InboxPolicyAction.Skip,
            new InboxEntry()));

        Assert.Equal(SquirrelBoxOperationExecutionState.Replayed, replayed.State);
        Assert.Null(replayed.Result);
        Assert.Equal(SquirrelBoxOperationExecutionState.Rejected, rejected.State);
        Assert.Equal(SquirrelBoxOperationExecutionState.Rejected, failed.State);
        Assert.Equal(SquirrelBoxOperationExecutionState.Skipped, skipped.State);
    }

    [Fact]
    public void Completion_reader_handles_null_completion()
    {
        var read = typeof(DefaultSquirrelBoxOperationService).GetMethod(
            "TryReadCompletion",
            BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(string));

        Assert.Null(read.Invoke(null, new object[] { null }));
    }

    [Fact]
    public async Task Explicit_operation_is_activated_when_not_registered()
    {
        using var provider = CreateRealProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<ISquirrelBoxOperationService>()
            .ExecuteAsync<UnregisteredOperation, BranchRequest, string>(new BranchRequest("value"));

        Assert.Equal("value", result.Result);
    }

    [Fact]
    public async Task Inline_success_does_not_complete_replaced_context()
    {
        using var setup = CreateControlledService(typeof(SuccessfulOperation), InboxExecutionMode.Inline);

        var result = await setup.Service
            .ExecuteAsync<SuccessfulOperation, BranchRequest, string>(new BranchRequest("success"));

        Assert.Equal("success", result.Result);
        await setup.Inbox.DidNotReceive().CompleteCurrentAsync(
            Arg.Any<InboxCompletion>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Inline_failure_does_not_fail_replaced_context()
    {
        using var setup = CreateControlledService(typeof(FailingOperation), InboxExecutionMode.Inline);

        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service
            .ExecuteAsync<FailingOperation, BranchRequest, string>(new BranchRequest("failure"))
            .AsTask());

        await setup.Inbox.DidNotReceive().FailCurrentAsync(
            Arg.Any<Exception>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deferred_failure_does_not_fail_replaced_context()
    {
        var scheduler = Substitute.For<ISquirrelBoxOperationDeferredScheduler>();
        scheduler.ScheduleAsync(
                Arg.Any<InboxContext>(),
                Arg.Any<SquirrelBoxOperationEnvelope>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("schedule failed"));
        using var setup = CreateControlledService(
            typeof(SuccessfulOperation),
            InboxExecutionMode.Deferred,
            scheduler);

        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service
            .ExecuteAsync<SuccessfulOperation, BranchRequest, string>(new BranchRequest("deferred"))
            .AsTask());

        await setup.Inbox.DidNotReceive().FailCurrentAsync(
            Arg.Any<Exception>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Inline_success_skips_completion_for_continued_open_state()
    {
        using var setup = CreateControlledService(
            typeof(SuccessfulOperation),
            InboxExecutionMode.Inline,
            openState: InboxOpenState.Continued);

        await setup.Service.ExecuteAsync<SuccessfulOperation, BranchRequest, string>(new BranchRequest("continued"));

        await setup.Inbox.DidNotReceive().CompleteCurrentAsync(
            Arg.Any<InboxCompletion>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Inline_failure_skips_failure_for_continued_open_state()
    {
        using var setup = CreateControlledService(
            typeof(FailingOperation),
            InboxExecutionMode.Inline,
            openState: InboxOpenState.Continued);

        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service
            .ExecuteAsync<FailingOperation, BranchRequest, string>(new BranchRequest("continued-failure"))
            .AsTask());

        await setup.Inbox.DidNotReceive().FailCurrentAsync(
            Arg.Any<Exception>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deferred_failure_skips_failure_for_continued_open_state()
    {
        var scheduler = Substitute.For<ISquirrelBoxOperationDeferredScheduler>();
        scheduler.ScheduleAsync(
                Arg.Any<InboxContext>(),
                Arg.Any<SquirrelBoxOperationEnvelope>(),
                Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("schedule failed"));
        using var setup = CreateControlledService(
            typeof(SuccessfulOperation),
            InboxExecutionMode.Deferred,
            scheduler,
            InboxOpenState.Continued);

        await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service
            .ExecuteAsync<SuccessfulOperation, BranchRequest, string>(new BranchRequest("continued-deferred"))
            .AsTask());

        await setup.Inbox.DidNotReceive().FailCurrentAsync(
            Arg.Any<Exception>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Operation_name_uses_type_name_when_full_name_is_unavailable()
    {
        var inbox = Substitute.For<IInboxService>();
        InboxOpenRequest captured = null;
        var open = new InboxOpenResult(InboxOpenState.MissingIdempotencyKey);
        inbox.OpenOrContinueAsync(Arg.Do<InboxOpenRequest>(request => captured = request), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(open));
        var policy = Substitute.For<IInboxPolicyResolver>();
        policy.Resolve(open).Returns(new InboxDecision(
            InboxOpenState.MissingIdempotencyKey,
            InboxPolicyAction.Skip,
            null));
        using var provider = new ServiceCollection().BuildServiceProvider();
        var service = new DefaultSquirrelBoxOperationService(
            provider,
            inbox,
            policy,
            Substitute.For<ISquirrelBoxOperationRegistry>(),
            Array.Empty<ISquirrelBoxOperationDeferredScheduler>());
        var method = typeof(DefaultSquirrelBoxOperationService).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "ExecuteAsync" && candidate.IsGenericMethodDefinition)
            .MakeGenericMethod(typeof(string), typeof(string));
        var genericParameter = typeof(GenericOperationName<>).GetGenericArguments()[0];
        var pending = (ValueTask<SquirrelBoxOperationResult<string>>)method.Invoke(
            service,
            new object[] { genericParameter, "request", CancellationToken.None })!;

        await pending;

        Assert.Equal("T", captured.Operation);
    }

    private static SquirrelBoxOperationResult<string> InvokeResult(MethodInfo method, InboxDecision decision)
        => (SquirrelBoxOperationResult<string>)method.Invoke(null, new object[] { decision, null })!;

    private static ServiceProvider CreateRealProvider()
    {
        var services = new ServiceCollection();
        services.AddSquirrelBox().UseInMemory();
        return services.BuildServiceProvider();
    }

    private static ControlledSetup CreateControlledService(
        Type operationType,
        InboxExecutionMode executionMode,
        ISquirrelBoxOperationDeferredScheduler scheduler = null,
        InboxOpenState openState = InboxOpenState.Opened)
    {
        var services = new ServiceCollection();
        services.AddSingleton(operationType, Activator.CreateInstance(operationType)!);
        var provider = services.BuildServiceProvider();
        var inbox = Substitute.For<IInboxService>();
        var entry = new InboxEntry
        {
            Id = Ulid.NewUlid(),
            ExecutionMode = executionMode
        };
        var context = new InboxContext(entry, "branch", ownsCompletion: true);
        var open = new InboxOpenResult(openState, context);
        inbox.OpenOrContinueAsync(Arg.Any<InboxOpenRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(open));
        inbox.Current.Returns((InboxContext)null);
        var policy = Substitute.For<IInboxPolicyResolver>();
        policy.Resolve(open).Returns(new InboxDecision(openState, InboxPolicyAction.Continue, entry));
        var service = new DefaultSquirrelBoxOperationService(
            provider,
            inbox,
            policy,
            Substitute.For<ISquirrelBoxOperationRegistry>(),
            scheduler is null ? Array.Empty<ISquirrelBoxOperationDeferredScheduler>() : [scheduler]);
        return new ControlledSetup(service, inbox, provider);
    }

    private sealed record ControlledSetup(
        DefaultSquirrelBoxOperationService Service,
        IInboxService Inbox,
        ServiceProvider Provider) : IDisposable
    {
        public void Dispose() => Provider.Dispose();
    }

    private sealed record BranchRequest(string Value);

    private sealed class GenericOperationName<T>;

    private sealed class UnregisteredOperation : SquirrelBoxOperation<BranchRequest, string>
    {
        protected override ValueTask<string> ExecuteAsync(
            BranchRequest request,
            SquirrelBoxOperationContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(request.Value);
    }

    private sealed class SuccessfulOperation : SquirrelBoxOperation<BranchRequest, string>
    {
        protected override ValueTask<string> ExecuteAsync(
            BranchRequest request,
            SquirrelBoxOperationContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(request.Value);
    }

    private sealed class FailingOperation : SquirrelBoxOperation<BranchRequest, string>
    {
        protected override ValueTask<string> ExecuteAsync(
            BranchRequest request,
            SquirrelBoxOperationContext context,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("operation failed");
    }
}
