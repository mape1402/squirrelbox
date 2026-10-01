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
    private static readonly ActionKey FailOnceKey = ActionKey.From("tests.squirrelbox.fail-once.v1");
    private static readonly ActionKey FailAlwaysKey = ActionKey.From("tests.squirrelbox.fail-always.v1");

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
    public async Task EnqueueCurrentAsync_applies_deferred_lane_and_retry_metadata()
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
            .Returns(new Guid("22222222-2222-2222-2222-222222222222"));

        var services = new ServiceCollection();
        services.AddSingleton(mule);
        services.AddSquirrelBox(options =>
        {
            options.AddInboxPolicy("orders-deferred", policy =>
            {
                policy.ExecutionMode = InboxExecutionMode.Deferred;
                policy.Deferred.Lane = "orders";
                policy.Deferred.MaxAttempts = 4;
                policy.Deferred.Delay = TimeSpan.FromSeconds(1);
                policy.Deferred.MaxDelay = TimeSpan.FromSeconds(10);
                policy.Deferred.Backoff = InboxRetryBackoff.Exponential;
                policy.Deferred.JitterRatio = 0.20;
            });
        }).UseInMemory();
        services.AddSquirrelBoxMule();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            source: "http",
            operation: "POST /orders",
            idempotencyKey: "order-policy",
            payload: new DeferredPayload("order-policy"),
            policyName: "orders-deferred"));

        await scope.ServiceProvider.GetRequiredService<IInboxMuleScheduler>()
            .EnqueueCurrentAsync(Key, new DeferredPayload("order-policy"));

        Assert.Equal("orders", capturedOptions.Lane);
        Assert.Equal("orders-deferred", capturedOptions.Metadata[SquirrelBoxMuleMetadata.DeferredPolicyName]);
        Assert.Equal("orders", capturedOptions.Metadata[SquirrelBoxMuleMetadata.DeferredLane]);
        Assert.Equal("4", capturedOptions.Metadata[SquirrelBoxMuleMetadata.DeferredMaxAttempts]);
        Assert.Equal("1000", capturedOptions.Metadata[SquirrelBoxMuleMetadata.DeferredRetryDelayMilliseconds]);
        Assert.Equal("10000", capturedOptions.Metadata[SquirrelBoxMuleMetadata.DeferredRetryMaxDelayMilliseconds]);
        Assert.Equal(InboxRetryBackoff.Exponential.ToString(), capturedOptions.Metadata[SquirrelBoxMuleMetadata.DeferredRetryBackoff]);
        Assert.Equal("0.2", capturedOptions.Metadata[SquirrelBoxMuleMetadata.DeferredRetryJitterRatio]);
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
    public async Task SquirrelBoxMuleAction_marks_inbox_retrying_when_attempt_is_not_terminal()
    {
        using var provider = CreateServiceProvider();
        var entryId = await OpenDeferredEntryAsync(provider, "order-retrying");

        await RunWithoutAmbientContextAsync(async () =>
        {
            using var workerScope = provider.CreateScope();
            var action = new CaptureDeferredPayloadAction(
                workerScope.ServiceProvider.GetRequiredService<IInboxService>(),
                workerScope.ServiceProvider.GetRequiredService<DeferredProbe>(),
                fail: true);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                action.ExecuteAsync(
                    CreateMuleContext(
                        workerScope.ServiceProvider,
                        entryId,
                        new DeferredPayload("order-retrying"),
                        attempts: 1,
                        maxAttempts: 3),
                    CancellationToken.None).AsTask());
        });

        using var verifyScope = provider.CreateScope();
        var entry = await verifyScope.ServiceProvider.GetRequiredService<IInboxService>().GetAsync(entryId);

        Assert.Equal(InboxStatus.Retrying, entry.Status);
        Assert.Equal("planned failure", entry.FailureDetails.ErrorMessage);
    }

    [Fact]
    public async Task SquirrelBoxMuleAction_fails_inbox_entry_when_attempt_reaches_max_attempts()
    {
        using var provider = CreateServiceProvider();
        var entryId = await OpenDeferredEntryAsync(provider, "order-terminal-retry");

        await RunWithoutAmbientContextAsync(async () =>
        {
            using var workerScope = provider.CreateScope();
            var action = new CaptureDeferredPayloadAction(
                workerScope.ServiceProvider.GetRequiredService<IInboxService>(),
                workerScope.ServiceProvider.GetRequiredService<DeferredProbe>(),
                fail: true);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                action.ExecuteAsync(
                    CreateMuleContext(
                        workerScope.ServiceProvider,
                        entryId,
                        new DeferredPayload("order-terminal-retry"),
                        attempts: 1,
                        maxAttempts: 2),
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

    [Fact]
    public async Task Hosted_mule_worker_retries_deferred_inbox_entry_and_completes_on_later_attempt()
    {
        using var host = CreateRetryHost();
        await host.StartAsync();

        var entryId = await OpenAndScheduleDeferredEntryAsync(
            host.Services,
            "order-retry-hosted",
            FailOnceKey,
            policyName: "retry-policy");

        var probe = host.Services.GetRequiredService<DeferredProbe>();
        await probe.WaitAsync();
        await WaitForInboxStatusAsync(host.Services, entryId, InboxStatus.Completed);
        await host.StopAsync();

        Assert.True(probe.AttemptCount("order-retry-hosted") >= 2);
        Assert.Equal("order-retry-hosted", probe.Values.Single());
    }

    [Fact]
    public async Task Hosted_mule_worker_fails_deferred_inbox_entry_after_terminal_retry()
    {
        using var host = CreateRetryHost();
        await host.StartAsync();

        var entryId = await OpenAndScheduleDeferredEntryAsync(
            host.Services,
            "order-terminal-hosted",
            FailAlwaysKey,
            policyName: "retry-policy");

        await WaitForInboxStatusAsync(host.Services, entryId, InboxStatus.Failed);
        await host.StopAsync();

        var probe = host.Services.GetRequiredService<DeferredProbe>();
        Assert.True(probe.AttemptCount("order-terminal-hosted") >= 2);
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

    private static IHost CreateRetryHost()
        => Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.Configure<MuleSettings>(settings =>
                {
                    settings.DispatchInterval = TimeSpan.FromMilliseconds(25);
                    settings.RetryDelay = TimeSpan.FromMilliseconds(25);
                    settings.MaxAttempts = 1;
                });

                services.AddSingleton<DeferredProbe>();
                services.AddSquirrelBox(options =>
                {
                    options.AddInboxPolicy("retry-policy", policy =>
                    {
                        policy.ExecutionMode = InboxExecutionMode.Deferred;
                        policy.Deferred.Lane = "retry-policy";
                        policy.Deferred.MaxAttempts = 2;
                        policy.Deferred.Delay = TimeSpan.FromMilliseconds(25);
                        policy.Deferred.InProgressTimeout = TimeSpan.FromMinutes(5);
                    });
                }).UseInMemory();
                services.AddSquirrelBoxMule();
                services.AddMule(mule => mule
                    .UseInMemory()
                    .AddActionsFromAssemblyContaining<FailOnceDeferredPayloadAction>());
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

    private static async Task<Ulid> OpenAndScheduleDeferredEntryAsync(
        IServiceProvider services,
        string id,
        ActionKey? key = null,
        string policyName = null)
    {
        using var scope = services.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        var opened = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            source: "http",
            operation: "POST /orders/deferred",
            idempotencyKey: id,
            payload: new DeferredPayload(id),
            correlationId: $"trace-{id}",
            executionMode: InboxExecutionMode.Deferred,
            policyName: policyName));

        await scope.ServiceProvider.GetRequiredService<IInboxMuleScheduler>()
            .EnqueueCurrentAsync(key ?? Key, new DeferredPayload(id));

        services.GetRequiredService<IInboxContextAccessor>().Current = null;
        return opened.Entry.Id;
    }

    private static MuleActionContext<DeferredPayload> CreateMuleContext(
        IServiceProvider services,
        Ulid entryId,
        DeferredPayload payload,
        int attempts = 0,
        int? maxAttempts = null)
    {
        var metadata = new Dictionary<string, string>
        {
            [SquirrelBoxMuleMetadata.InboxEntryId] = entryId.ToString(),
            [SquirrelBoxMuleMetadata.IdempotencyKey] = payload.Id
        };
        if (maxAttempts is not null)
            metadata[SquirrelBoxMuleMetadata.DeferredMaxAttempts] = maxAttempts.Value.ToString();

        var action = new DurableAction
        {
            Key = Key,
            Attempts = attempts,
            Metadata = JsonSerializer.Serialize(metadata)
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

    [MuleAction("tests.squirrelbox.fail-once.v1")]
    private sealed class FailOnceDeferredPayloadAction : SquirrelBoxMuleAction<DeferredPayload>
    {
        private readonly DeferredProbe _probe;

        public FailOnceDeferredPayloadAction(IInboxService inbox, DeferredProbe probe)
            : base(inbox)
        {
            _probe = probe;
        }

        protected override ValueTask ExecuteInboxAsync(
            SquirrelBoxMuleActionContext<DeferredPayload> context,
            CancellationToken cancellationToken)
        {
            var attempt = _probe.IncrementAttempt(context.Payload.Id);
            if (attempt == 1)
                throw new InvalidOperationException("planned transient failure");

            _probe.Record(context.Payload.Id, context.Inbox.Entry.Id, context.Inbox.Owner);
            return ValueTask.CompletedTask;
        }
    }

    [MuleAction("tests.squirrelbox.fail-always.v1")]
    private sealed class FailAlwaysDeferredPayloadAction : SquirrelBoxMuleAction<DeferredPayload>
    {
        private readonly DeferredProbe _probe;

        public FailAlwaysDeferredPayloadAction(IInboxService inbox, DeferredProbe probe)
            : base(inbox)
        {
            _probe = probe;
        }

        protected override ValueTask ExecuteInboxAsync(
            SquirrelBoxMuleActionContext<DeferredPayload> context,
            CancellationToken cancellationToken)
        {
            _probe.IncrementAttempt(context.Payload.Id);
            throw new InvalidOperationException("planned terminal failure");
        }
    }

    private sealed class DeferredProbe
    {
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);

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

        public int IncrementAttempt(string value)
            => _attempts.AddOrUpdate(value, 1, (_, current) => current + 1);

        public int AttemptCount(string value)
            => _attempts.TryGetValue(value, out var count) ? count : 0;

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
