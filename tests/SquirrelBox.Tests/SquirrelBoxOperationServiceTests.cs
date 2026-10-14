using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SquirrelBox.InMemory;

namespace SquirrelBox.Tests;

public sealed class SquirrelBoxOperationServiceTests
{
    [Fact]
    public async Task ExecuteAsync_runs_operation_inline_and_completes_inbox_entry()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<ISquirrelBoxOperationService>()
            .ExecuteAsync<CreateOrderOperation, CreateOrderRequest, CreateOrderResult>(
                new CreateOrderRequest("order-1"),
                CancellationToken.None);

        var entry = await scope.ServiceProvider
            .GetRequiredService<IInboxService>()
            .GetAsync(result.Context.Entry.Id);

        Assert.True(result.Executed);
        Assert.Equal("created:order-1", result.Result.Value);
        Assert.Equal(InboxStatus.Completed, entry.Status);
        Assert.Null(scope.ServiceProvider.GetRequiredService<IInboxService>().Current);
    }

    [Fact]
    public async Task ExecuteAsync_defers_operation_and_releases_current_context()
    {
        using var provider = CreateProvider(options => options.DefaultExecutionMode = InboxExecutionMode.Deferred);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<ISquirrelBoxOperationService>()
            .ExecuteAsync<CreateOrderOperation, CreateOrderRequest, CreateOrderResult>(
                new CreateOrderRequest("order-1"),
                CancellationToken.None);

        var scheduler = scope.ServiceProvider.GetRequiredService<FakeOperationScheduler>();
        var entry = await scope.ServiceProvider.GetRequiredService<IInboxService>().GetAsync(result.Context.Entry.Id);

        Assert.True(result.Deferred);
        Assert.Single(scheduler.Envelopes);
        Assert.Equal(typeof(CreateOrderOperation).AssemblyQualifiedName, scheduler.Envelopes.Single().OperationType);
        Assert.Equal(InboxStatus.Started, entry.Status);
        Assert.Null(scope.ServiceProvider.GetRequiredService<IInboxService>().Current);
    }

    [Fact]
    public async Task ExecuteAsync_replays_completed_operation_without_executing_again()
    {
        using var provider = CreateProvider();

        using var firstScope = provider.CreateScope();
        var first = await firstScope.ServiceProvider
            .GetRequiredService<ISquirrelBoxOperationService>()
            .ExecuteAsync<CreateOrderOperation, CreateOrderRequest, CreateOrderResult>(
                new CreateOrderRequest("order-replay"),
                CancellationToken.None);

        using var secondScope = provider.CreateScope();
        var second = await secondScope.ServiceProvider
            .GetRequiredService<ISquirrelBoxOperationService>()
            .ExecuteAsync<CreateOrderOperation, CreateOrderRequest, CreateOrderResult>(
                new CreateOrderRequest("order-replay"),
                CancellationToken.None);

        Assert.True(first.Executed);
        Assert.Equal(SquirrelBoxOperationExecutionState.Replayed, second.State);
        Assert.Equal("created:order-replay", second.Result.Value);
        Assert.Single(provider.GetRequiredService<OrderProbe>().Requests);
    }

    [Fact]
    public async Task ExecuteAsync_marks_entry_failed_when_inline_operation_throws()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISquirrelBoxOperationService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteAsync<FailingOrderOperation, FailingOrderRequest, CreateOrderResult>(
                new FailingOrderRequest("order-fails"),
                CancellationToken.None).AsTask());

        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        Assert.Equal(InboxStatus.Failed, inbox.LastContext.Entry.Status);
        Assert.Equal("operation failed", inbox.LastContext.Entry.FailureDetails.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_marks_entry_failed_when_deferred_scheduler_throws()
    {
        using var provider = CreateProvider(options => options.DefaultExecutionMode = InboxExecutionMode.Deferred);
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<FakeOperationScheduler>().ExceptionToThrow =
            new InvalidOperationException("scheduler failed");
        var service = scope.ServiceProvider.GetRequiredService<ISquirrelBoxOperationService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteAsync<CreateOrderOperation, CreateOrderRequest, CreateOrderResult>(
                new CreateOrderRequest("order-scheduler-fails"),
                CancellationToken.None).AsTask());

        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        Assert.Equal(InboxStatus.Failed, inbox.LastContext.Entry.Status);
        Assert.Equal("scheduler failed", inbox.LastContext.Entry.FailureDetails.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_fails_deferred_operation_when_scheduler_is_missing()
    {
        using var provider = CreateProvider(
            options => options.DefaultExecutionMode = InboxExecutionMode.Deferred,
            registerScheduler: false);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISquirrelBoxOperationService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteAsync<CreateOrderOperation, CreateOrderRequest, CreateOrderResult>(
                new CreateOrderRequest("order-no-scheduler"),
                CancellationToken.None).AsTask());

        Assert.Equal(InboxStatus.Failed, scope.ServiceProvider.GetRequiredService<IInboxService>().LastContext.Entry.Status);
    }

    [Fact]
    public async Task ExecuteAsync_completes_with_empty_completion_when_operation_returns_null()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<ISquirrelBoxOperationService>()
            .ExecuteAsync<NullOrderOperation, NullOrderRequest, CreateOrderResult>(
                new NullOrderRequest("order-null"),
                CancellationToken.None);

        var entry = await scope.ServiceProvider.GetRequiredService<IInboxService>().GetAsync(result.Context.Entry.Id);

        Assert.True(result.Executed);
        Assert.Null(result.Result);
        Assert.Null(entry.Completion.ResultPayload);
    }

    [Fact]
    public async Task ExecuteAsync_can_discover_unique_operation_by_request_and_result()
    {
        using var provider = CreateProvider(options => options.ScanAssemblyContaining<CreateOrderOperation>());
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<ISquirrelBoxOperationService>()
            .ExecuteAsync<CreateOrderRequest, CreateOrderResult>(new CreateOrderRequest("order-1"));

        Assert.True(result.Executed);
        Assert.Equal("created:order-1", result.Result.Value);
    }

    [Fact]
    public async Task ExecuteAsync_throws_when_no_operation_is_discovered_for_request_and_result()
    {
        using var provider = CreateProvider(options => options.ScanAssemblyContaining<CreateOrderOperation>());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISquirrelBoxOperationService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteAsync<UnknownOrderRequest, CreateOrderResult>(new UnknownOrderRequest("missing")).AsTask());
    }

    [Fact]
    public async Task ExecuteAsync_throws_when_multiple_operations_match_request_and_result()
    {
        using var provider = CreateProvider(options => options.ScanAssemblyContaining<DuplicateOrderOperationA>());
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISquirrelBoxOperationService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteAsync<DuplicateOrderRequest, DuplicateOrderResult>(new DuplicateOrderRequest("duplicate")).AsTask());
    }

    [Fact]
    public async Task ExecuteAsync_returns_skipped_when_policy_does_not_execute_opened_operation()
    {
        using var provider = CreateProvider(options => options.Policies.Opened = InboxPolicyAction.Skip);
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider
            .GetRequiredService<ISquirrelBoxOperationService>()
            .ExecuteAsync<CreateOrderOperation, CreateOrderRequest, CreateOrderResult>(new CreateOrderRequest("order-skip"));

        Assert.Equal(SquirrelBoxOperationExecutionState.Skipped, result.State);
        Assert.Null(result.Result);
        Assert.Empty(provider.GetRequiredService<OrderProbe>().Requests);
    }

    [Fact]
    public async Task ExecuteAsync_replays_empty_completion_as_default_result()
    {
        using var provider = CreateProvider();

        using (var firstScope = provider.CreateScope())
        {
            await firstScope.ServiceProvider
                .GetRequiredService<ISquirrelBoxOperationService>()
                .ExecuteAsync<NullOrderOperation, NullOrderRequest, CreateOrderResult>(new NullOrderRequest("order-null-replay"));
        }

        using var secondScope = provider.CreateScope();
        var second = await secondScope.ServiceProvider
            .GetRequiredService<ISquirrelBoxOperationService>()
            .ExecuteAsync<NullOrderOperation, NullOrderRequest, CreateOrderResult>(new NullOrderRequest("order-null-replay"));

        Assert.Equal(SquirrelBoxOperationExecutionState.Replayed, second.State);
        Assert.Null(second.Result);
    }

    [Fact]
    public async Task ContinueAsync_executes_deferred_operation_envelope_in_current_context()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        var opened = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            source: "operation",
            operation: typeof(CreateOrderOperation).FullName,
            idempotencyKey: "order-1",
            payload: new CreateOrderRequest("order-1"),
            executionMode: InboxExecutionMode.Deferred));

        var envelope = new SquirrelBoxOperationEnvelope
        {
            OperationType = typeof(CreateOrderOperation).AssemblyQualifiedName,
            RequestType = typeof(CreateOrderRequest).AssemblyQualifiedName,
            ResultType = typeof(CreateOrderResult).AssemblyQualifiedName,
            RequestJson = JsonSerializer.Serialize(new CreateOrderRequest("order-1"), JsonOptions)
        };

        var result = await scope.ServiceProvider
            .GetRequiredService<ISquirrelBoxOperationService>()
            .ContinueAsync(envelope);

        Assert.Equal("created:order-1", Assert.IsType<CreateOrderResult>(result).Value);
        Assert.Equal(opened.Entry.Id, inbox.Current.Entry.Id);
    }

    [Fact]
    public async Task ContinueAsync_rejects_unresolvable_envelope_types()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ISquirrelBoxOperationService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ContinueAsync(new SquirrelBoxOperationEnvelope
            {
                OperationType = "missing.operation",
                RequestType = typeof(CreateOrderRequest).AssemblyQualifiedName,
                ResultType = typeof(CreateOrderResult).AssemblyQualifiedName,
                RequestJson = "{}"
            }).AsTask());
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static ServiceProvider CreateProvider(
        Action<SquirrelBoxOptions> configure = null,
        bool registerScheduler = true)
    {
        var services = new ServiceCollection();
        var scheduler = new FakeOperationScheduler();
        services.AddSingleton<OrderProbe>();
        services.AddSingleton(scheduler);
        services.AddScoped<CreateOrderOperation>();
        services.AddScoped<FailingOrderOperation>();
        services.AddScoped<NullOrderOperation>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISquirrelBoxOperationDeferredScheduler>(scheduler));
        if (!registerScheduler)
            services.RemoveAll<ISquirrelBoxOperationDeferredScheduler>();
        services.AddSquirrelBox(configure).UseInMemory();
        return services.BuildServiceProvider();
    }

    private sealed record CreateOrderRequest(string Id);

    private sealed record UnknownOrderRequest(string Id);

    private sealed record FailingOrderRequest(string Id);

    private sealed record NullOrderRequest(string Id);

    private sealed record CreateOrderResult(string Value);

    private sealed record DuplicateOrderRequest(string Id);

    private sealed record DuplicateOrderResult(string Value);

    private sealed class CreateOrderOperation : SquirrelBoxOperation<CreateOrderRequest, CreateOrderResult>
    {
        protected override ValueTask<CreateOrderResult> ExecuteAsync(
            CreateOrderRequest request,
            SquirrelBoxOperationContext context,
            CancellationToken cancellationToken)
        {
            context.Services.GetRequiredService<OrderProbe>().Requests.Add(request.Id);
            return ValueTask.FromResult(new CreateOrderResult($"created:{request.Id}"));
        }
    }

    private sealed class FailingOrderOperation : SquirrelBoxOperation<FailingOrderRequest, CreateOrderResult>
    {
        protected override ValueTask<CreateOrderResult> ExecuteAsync(
            FailingOrderRequest request,
            SquirrelBoxOperationContext context,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("operation failed");
    }

    private sealed class NullOrderOperation : SquirrelBoxOperation<NullOrderRequest, CreateOrderResult>
    {
        protected override ValueTask<CreateOrderResult> ExecuteAsync(
            NullOrderRequest request,
            SquirrelBoxOperationContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult<CreateOrderResult>(null);
    }

    private sealed class DuplicateOrderOperationA : SquirrelBoxOperation<DuplicateOrderRequest, DuplicateOrderResult>
    {
        protected override ValueTask<DuplicateOrderResult> ExecuteAsync(
            DuplicateOrderRequest request,
            SquirrelBoxOperationContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new DuplicateOrderResult($"a:{request.Id}"));
    }

    private sealed class DuplicateOrderOperationB : SquirrelBoxOperation<DuplicateOrderRequest, DuplicateOrderResult>
    {
        protected override ValueTask<DuplicateOrderResult> ExecuteAsync(
            DuplicateOrderRequest request,
            SquirrelBoxOperationContext context,
            CancellationToken cancellationToken)
            => ValueTask.FromResult(new DuplicateOrderResult($"b:{request.Id}"));
    }

    private sealed class OrderProbe
    {
        public List<string> Requests { get; } = [];
    }

    private sealed class FakeOperationScheduler : ISquirrelBoxOperationDeferredScheduler
    {
        public List<SquirrelBoxOperationEnvelope> Envelopes { get; } = [];

        public Exception ExceptionToThrow { get; set; }

        public ValueTask ScheduleAsync(
            InboxContext context,
            SquirrelBoxOperationEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null)
                throw ExceptionToThrow;

            Envelopes.Add(envelope);
            return ValueTask.CompletedTask;
        }
    }
}
