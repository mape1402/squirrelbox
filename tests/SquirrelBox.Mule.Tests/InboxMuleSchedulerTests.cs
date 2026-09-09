using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Mule;
using Mule.InMemory;
using NSubstitute;
using SquirrelBox.InMemory;

namespace SquirrelBox.Mule.Tests;

public sealed class InboxMuleSchedulerTests
{
    private static readonly ActionKey Key = ActionKey.From("tests.squirrelbox.deferred.v1");

    [Fact]
    public async Task EnqueueCurrentAsync_attaches_inbox_metadata_to_mule_action()
    {
        var mule = Substitute.For<IMuleClient>();
        EnqueueOptions capturedOptions = null;
        mule.EnqueueAsync(
                Arg.Any<ActionKey>(),
                Arg.Any<DeferredPayload>(),
                Arg.Do<Action<EnqueueOptions>>(configure =>
                {
                    capturedOptions = new EnqueueOptions();
                    configure(capturedOptions);
                }),
                Arg.Any<CancellationToken>())
            .Returns(new Guid("11111111-1111-1111-1111-111111111111"));

        var services = new ServiceCollection();
        services.AddSingleton(mule);
        services.AddSquirrelBox().UseInMemory();
        services.AddSquirrelBoxMule();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        var opened = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            source: "http",
            operation: "POST /orders",
            idempotencyKey: "order-1",
            payload: new DeferredPayload("order-1"),
            correlationId: "trace-1",
            executionMode: InboxExecutionMode.Deferred));

        await scope.ServiceProvider.GetRequiredService<IInboxMuleScheduler>()
            .EnqueueCurrentAsync(Key, new DeferredPayload("order-1"));

        Assert.Equal("trace-1", capturedOptions.CorrelationId);
        Assert.Equal(opened.Entry.Id.ToString(), capturedOptions.DeduplicationKey);
        Assert.Equal(opened.Entry.Id.ToString(), capturedOptions.Metadata[SquirrelBoxMuleMetadata.InboxEntryId]);
        Assert.Equal("order-1", capturedOptions.Metadata[SquirrelBoxMuleMetadata.IdempotencyKey]);
    }

    [Fact]
    public async Task SquirrelBoxMuleAction_continues_context_and_completes_inbox_entry()
    {
        using var provider = CreateServiceProvider();
        var entryId = await OpenDeferredEntryAsync(provider, "order-action");

        await RunWithoutAmbientContextAsync(async () =>
        {
            using var workerScope = provider.CreateScope();
            var action = new CaptureDeferredPayloadAction(
                workerScope.ServiceProvider.GetRequiredService<IInboxService>(),
                workerScope.ServiceProvider.GetRequiredService<DeferredProbe>());

            await action.ExecuteAsync(
                CreateMuleContext(workerScope.ServiceProvider, entryId, new DeferredPayload("order-action")),
                CancellationToken.None);
        });

        using var verifyScope = provider.CreateScope();
        var entry = await verifyScope.ServiceProvider.GetRequiredService<IInboxService>().GetAsync(entryId);
        var probe = provider.GetRequiredService<DeferredProbe>();

        Assert.Equal(InboxStatus.Completed, entry.Status);
        Assert.Equal("mule", probe.Owners.Single());
        Assert.Equal(entryId, probe.EntryIds.Single());
        Assert.Equal("processed", entry.Completion.Metadata["result"]);
    }

    [Fact]
    public async Task SquirrelBoxMuleAction_fails_inbox_entry_when_execution_fails()
    {
        using var provider = CreateServiceProvider();
        var entryId = await OpenDeferredEntryAsync(provider, "order-fail");

        await RunWithoutAmbientContextAsync(async () =>
        {
            using var workerScope = provider.CreateScope();
            var action = new CaptureDeferredPayloadAction(
                workerScope.ServiceProvider.GetRequiredService<IInboxService>(),
                workerScope.ServiceProvider.GetRequiredService<DeferredProbe>(),
                fail: true);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                action.ExecuteAsync(
                    CreateMuleContext(workerScope.ServiceProvider, entryId, new DeferredPayload("order-fail")),
                    CancellationToken.None).AsTask());
        });

        using var verifyScope = provider.CreateScope();
        var entry = await verifyScope.ServiceProvider.GetRequiredService<IInboxService>().GetAsync(entryId);

        Assert.Equal(InboxStatus.Failed, entry.Status);
        Assert.Equal("planned failure", entry.FailureDetails.ErrorMessage);
    }

    [Fact]
    public async Task Hosted_mule_worker_executes_deferred_inbox_entry_end_to_end()
    {
        using var host = CreateHost();
        await host.StartAsync();

        var entryId = await OpenAndScheduleDeferredEntryAsync(host.Services, "order-hosted");

        var probe = host.Services.GetRequiredService<DeferredProbe>();
        await probe.WaitAsync();
        await WaitForInboxStatusAsync(host.Services, entryId, InboxStatus.Completed);
        await host.StopAsync();

        var action = Assert.Single(host.Services.GetRequiredService<IInMemoryMule>().Actions);
        var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(action.Metadata);

        Assert.Equal(DurableActionStatus.Completed, action.Status);
        Assert.Equal(entryId.ToString(), metadata[SquirrelBoxMuleMetadata.InboxEntryId]);
        Assert.Equal(entryId, probe.EntryIds.Single());
        Assert.Equal("order-hosted", probe.Values.Single());
    }

    [Fact]
    public async Task Hosted_mule_worker_executes_deferred_declared_operation_end_to_end()
    {
        using var host = CreateOperationHost();
        await host.StartAsync();

        Ulid entryId;
        using (var scope = host.Services.CreateScope())
        {
            var result = await scope.ServiceProvider
                .GetRequiredService<ISquirrelBoxOperationService>()
                .ExecuteAsync<CreateDeferredOrderOperation, DeferredOrderRequest, DeferredOrderResult>(
                    new DeferredOrderRequest("order-operation"),
                    CancellationToken.None);

            entryId = result.Context.Entry.Id;
            Assert.True(result.Deferred);
            Assert.Null(scope.ServiceProvider.GetRequiredService<IInboxService>().Current);
        }

        var probe = host.Services.GetRequiredService<DeferredProbe>();
        await probe.WaitAsync();
        await WaitForInboxStatusAsync(host.Services, entryId, InboxStatus.Completed);
        await host.StopAsync();

        Assert.Equal("order-operation", probe.Values.Single());
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DeferredProbe>();
        services.AddSquirrelBox().UseInMemory();
        services.AddSquirrelBoxMule();
        return services.BuildServiceProvider();
    }

    private static IHost CreateHost()
        => Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.Configure<MuleSettings>(settings =>
                {
                    settings.DispatchInterval = TimeSpan.FromMilliseconds(50);
                    settings.RetryDelay = TimeSpan.FromMilliseconds(50);
                    settings.MaxAttempts = 1;
                });

                services.AddSingleton<DeferredProbe>();
                services.AddSquirrelBox().UseInMemory();
                services.AddSquirrelBoxMule();
                services.AddMule(mule => mule
                    .UseInMemory()
                    .AddActionsFromAssemblyContaining<CaptureDeferredPayloadAction>());
            })
            .Build();

    private static IHost CreateOperationHost()
        => Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.Configure<MuleSettings>(settings =>
                {
                    settings.DispatchInterval = TimeSpan.FromMilliseconds(50);
                    settings.RetryDelay = TimeSpan.FromMilliseconds(50);
                    settings.MaxAttempts = 1;
                });

                services.AddSingleton<DeferredProbe>();
                services.AddScoped<CreateDeferredOrderOperation>();
                services.AddSquirrelBox(options => options.DefaultExecutionMode = InboxExecutionMode.Deferred).UseInMemory();
                services.AddSquirrelBoxMule();
                services.AddMule(mule => mule
                    .UseInMemory()
                    .AddActionsFromAssemblyContaining<SquirrelBoxOperationMuleAction>());
            })
            .Build();

    private static async Task<Ulid> OpenDeferredEntryAsync(IServiceProvider services, string id)
    {
        using var scope = services.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        var opened = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            source: "http",
            operation: "POST /orders/deferred",
            idempotencyKey: id,
            payload: new DeferredPayload(id),
            correlationId: $"trace-{id}",
            executionMode: InboxExecutionMode.Deferred));

        services.GetRequiredService<IInboxContextAccessor>().Current = null;
        return opened.Entry.Id;
    }

    private static async Task<Ulid> OpenAndScheduleDeferredEntryAsync(IServiceProvider services, string id)
    {
        using var scope = services.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        var opened = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            source: "http",
            operation: "POST /orders/deferred",
            idempotencyKey: id,
            payload: new DeferredPayload(id),
            correlationId: $"trace-{id}",
            executionMode: InboxExecutionMode.Deferred));

        await scope.ServiceProvider.GetRequiredService<IInboxMuleScheduler>()
            .EnqueueCurrentAsync(Key, new DeferredPayload(id));

        services.GetRequiredService<IInboxContextAccessor>().Current = null;
        return opened.Entry.Id;
    }

    private static MuleActionContext<DeferredPayload> CreateMuleContext(
        IServiceProvider services,
        Ulid entryId,
        DeferredPayload payload)
    {
        var action = new DurableAction
        {
            Key = Key,
            Metadata = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                [SquirrelBoxMuleMetadata.InboxEntryId] = entryId.ToString(),
                [SquirrelBoxMuleMetadata.IdempotencyKey] = payload.Id
            })
        };

        return new MuleActionContext<DeferredPayload>(action, services, payload);
    }

    private static async Task WaitForInboxStatusAsync(
        IServiceProvider services,
        Ulid entryId,
        InboxStatus status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        while (!timeout.IsCancellationRequested)
        {
            using var scope = services.CreateScope();
            var entry = await scope.ServiceProvider.GetRequiredService<IInboxService>()
                .GetAsync(entryId, timeout.Token);

            if (entry.Status == status)
                return;

            await Task.Delay(25, timeout.Token);
        }

        throw new TimeoutException($"Inbox entry '{entryId}' did not reach '{status}'.");
    }

    private static async Task RunWithoutAmbientContextAsync(Func<Task> action)
    {
        Task task;
        using (ExecutionContext.SuppressFlow())
        {
            task = Task.Run(action);
        }

        await task;
    }

    [MuleAction("tests.squirrelbox.deferred.v1")]
    private sealed class CaptureDeferredPayloadAction : SquirrelBoxMuleAction<DeferredPayload>
    {
        private readonly bool _fail;
        private readonly DeferredProbe _probe;

        public CaptureDeferredPayloadAction(IInboxService inbox, DeferredProbe probe)
            : this(inbox, probe, fail: false)
        {
        }

        public CaptureDeferredPayloadAction(IInboxService inbox, DeferredProbe probe, bool fail)
            : base(inbox)
        {
            _probe = probe;
            _fail = fail;
        }

        protected override ValueTask ExecuteInboxAsync(
            SquirrelBoxMuleActionContext<DeferredPayload> context,
            CancellationToken cancellationToken)
        {
            if (_fail)
                throw new InvalidOperationException("planned failure");

            _probe.Record(context.Payload.Id, context.Inbox.Entry.Id, context.Inbox.Owner);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask<InboxCompletion> CreateCompletionAsync(
            SquirrelBoxMuleActionContext<DeferredPayload> context,
            CancellationToken cancellationToken)
        {
            var completion = new InboxCompletion();
            completion.Metadata["result"] = "processed";
            return ValueTask.FromResult(completion);
        }
    }

    private sealed class DeferredProbe
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentBag<Ulid> EntryIds { get; } = new();

        public ConcurrentBag<string> Owners { get; } = new();

        public ConcurrentBag<string> Values { get; } = new();

        public void Record(string value, Ulid entryId, string owner)
        {
            Values.Add(value);
            EntryIds.Add(entryId);
            Owners.Add(owner);
            _completion.TrySetResult();
        }

        public Task WaitAsync()
            => _completion.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private sealed record DeferredPayload(string Id);

    private sealed record DeferredOrderRequest(string Id);

    private sealed record DeferredOrderResult(string Id);

    private sealed class CreateDeferredOrderOperation : SquirrelBoxOperation<DeferredOrderRequest, DeferredOrderResult>
    {
        protected override ValueTask<DeferredOrderResult> ExecuteAsync(
            DeferredOrderRequest request,
            SquirrelBoxOperationContext context,
            CancellationToken cancellationToken)
        {
            context.Services.GetRequiredService<DeferredProbe>()
                .Record(request.Id, context.InboxContext.Entry.Id, context.InboxContext.Owner);

            return ValueTask.FromResult(new DeferredOrderResult(request.Id));
        }
    }
}
