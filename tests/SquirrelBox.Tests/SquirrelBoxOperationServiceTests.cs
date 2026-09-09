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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static ServiceProvider CreateProvider(Action<SquirrelBoxOptions> configure = null)
    {
        var services = new ServiceCollection();
        var scheduler = new FakeOperationScheduler();
        services.AddSingleton<OrderProbe>();
        services.AddSingleton(scheduler);
        services.AddScoped<CreateOrderOperation>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISquirrelBoxOperationDeferredScheduler>(scheduler));
        services.AddSquirrelBox(configure).UseInMemory();
        return services.BuildServiceProvider();
    }

    private sealed record CreateOrderRequest(string Id);

    private sealed record CreateOrderResult(string Value);

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

    private sealed class OrderProbe
    {
        public List<string> Requests { get; } = [];
    }

    private sealed class FakeOperationScheduler : ISquirrelBoxOperationDeferredScheduler
    {
        public List<SquirrelBoxOperationEnvelope> Envelopes { get; } = [];

        public ValueTask ScheduleAsync(
            InboxContext context,
            SquirrelBoxOperationEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Envelopes.Add(envelope);
            return ValueTask.CompletedTask;
        }
    }
}
