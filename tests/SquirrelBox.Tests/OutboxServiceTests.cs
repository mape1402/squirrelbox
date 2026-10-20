using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using SquirrelBox.InMemory;

namespace SquirrelBox.Tests;

public sealed class OutboxServiceTests
{
    [Fact]
    public async Task EnqueueAsync_persists_envelope_and_emits_event()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        Assert.Null(outbox.Current);
        var envelope = await outbox.EnqueueAsync(new OutboxEnqueueRequest
        {
            Transport = "test",
            Operation = "orders.created",
            Destination = "orders",
            Payload = new TestMessage("order-1"),
            CorrelationId = "trace-1"
        });

        var stored = await outbox.GetAsync(envelope.Id);
        var events = provider.GetRequiredService<ISquirrelBoxEventSink>().GetRecent();

        Assert.Equal(OutboxStatus.Pending, stored.Status);
        Assert.Equal("test", stored.Transport);
        Assert.Equal("orders.created", stored.Operation);
        Assert.Contains(events, item => item.Name == SquirrelBoxEventNames.OutboxEnqueued &&
                                       item.SubjectId == envelope.Id.ToString());
    }

    [Fact]
    public async Task PublishAsync_delivers_with_registered_transport_publisher_and_updates_status()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        var envelope = await outbox.EnqueueAsync(new OutboxEnqueueRequest
        {
            Transport = "test",
            Operation = "orders.created",
            Destination = "orders",
            Payload = new TestMessage("order-2")
        });

        var result = await outbox.PublishAsync(envelope.Id);
        var stored = await outbox.GetAsync(envelope.Id);
        var publisher = provider.GetRequiredService<TestOutboxPublisher>();
        var events = provider.GetRequiredService<ISquirrelBoxEventSink>().GetRecent();

        Assert.True(result.Succeeded);
        Assert.Equal(OutboxStatus.Published, stored.Status);
        Assert.Equal(envelope.Id, publisher.Published.Single().Id);
        Assert.Contains(events, item => item.Name == SquirrelBoxEventNames.OutboxPublished &&
                                       item.SubjectId == envelope.Id.ToString());
    }

    [Fact]
    public async Task PublishAsync_marks_failed_when_transport_publisher_fails()
    {
        using var provider = CreateProvider(fail: true);
        using var scope = provider.CreateScope();

        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        var envelope = await outbox.EnqueueAsync(new OutboxEnqueueRequest
        {
            Transport = "test",
            Operation = "orders.created",
            Destination = "orders",
            Payload = new TestMessage("order-3")
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => outbox.PublishAsync(envelope.Id).AsTask());
        var stored = await outbox.GetAsync(envelope.Id);

        Assert.Equal(OutboxStatus.Failed, stored.Status);
        Assert.Equal(1, stored.Attempts);
        Assert.Contains("planned", stored.Failure.Details);
    }

    [Fact]
    public async Task PublishAsync_returns_success_without_republishing_terminal_envelope()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        var envelope = await outbox.EnqueueAsync(new OutboxEnqueueRequest
        {
            Transport = "test",
            Operation = "orders.created",
            Destination = "orders",
            Payload = new TestMessage("order-terminal")
        });

        await outbox.PublishAsync(envelope.Id);
        var second = await outbox.PublishAsync(envelope.Id);

        Assert.True(second.Succeeded);
        Assert.Single(provider.GetRequiredService<TestOutboxPublisher>().Published);
    }

    [Fact]
    public async Task QueryAsync_accepts_null_query()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        await outbox.EnqueueAsync(new OutboxEnqueueRequest
        {
            Transport = "test",
            Operation = "orders.created",
            Destination = "orders",
            Payload = new TestMessage("order-query")
        });

        var envelopes = await outbox.QueryAsync(null);

        Assert.Single(envelopes);
    }

    [Fact]
    public async Task EnqueueAsync_requires_deferred_scheduler_when_configured()
    {
        using var provider = CreateProvider(configure: options => options.Outbox.RequireDeferredScheduler = true);
        using var scope = provider.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => outbox.EnqueueAsync(new OutboxEnqueueRequest
        {
            Transport = "test",
            Operation = "orders.created",
            Destination = "orders",
            Payload = new TestMessage("order-requires-scheduler")
        }).AsTask());
    }

    [Fact]
    public async Task EnqueueAsync_rejects_custom_profile_that_returns_empty_payload()
    {
        using var provider = CreateProvider(configure: options => options.ScanAssemblyContaining<InvalidOutboxProfile>());
        using var scope = provider.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => outbox.EnqueueAsync(new OutboxEnqueueRequest
        {
            Transport = "invalid",
            Operation = "orders.invalid",
            Destination = "orders",
            Payload = new TestMessage("order-invalid")
        }).AsTask());
    }

    [Fact]
    public async Task EnqueueAsync_schedules_envelope_when_deferred_scheduler_is_registered()
    {
        using var provider = CreateProvider(registerScheduler: true);
        using var scope = provider.CreateScope();

        var envelope = await scope.ServiceProvider.GetRequiredService<IOutboxService>()
            .EnqueueAsync(new OutboxEnqueueRequest
            {
                Transport = "test",
                Operation = "orders.scheduled",
                Payload = new TestMessage("scheduled")
            });

        Assert.Equal(envelope.Id, Assert.Single(provider.GetRequiredService<TestOutboxScheduler>().Scheduled).Id);
    }

    [Fact]
    public async Task PublishAsync_preserves_failed_result_without_failure_details()
    {
        using var provider = CreateProvider();
        provider.GetRequiredService<TestOutboxPublisher>().Result = new OutboxPublishResult(false);
        using var scope = provider.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        var envelope = await outbox.EnqueueAsync(new OutboxEnqueueRequest
        {
            Transport = "test",
            Operation = "orders.rejected",
            Payload = new TestMessage("rejected")
        });

        var result = await outbox.PublishAsync(envelope.Id);
        var stored = await outbox.GetAsync(envelope.Id);

        Assert.False(result.Succeeded);
        Assert.Null(result.Failure);
        Assert.Equal(OutboxStatus.Failed, stored.Status);
    }

    [Fact]
    public async Task PublishAsync_fails_when_transport_publisher_is_missing()
    {
        using var provider = CreateProvider(registerPublisher: false);
        using var scope = provider.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxService>();
        var envelope = await outbox.EnqueueAsync(new OutboxEnqueueRequest
        {
            Transport = "missing",
            Operation = "orders.missing",
            Payload = new TestMessage("missing")
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => outbox.PublishAsync(envelope.Id).AsTask());
    }

    [Fact]
    public void Envelope_validation_rejects_empty_serialized_payload()
    {
        var method = typeof(DefaultOutboxService).GetMethod(
            "ValidateEnvelope",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        var envelope = new OutboxEnvelope
        {
            Transport = "test",
            Operation = "orders.empty",
            PayloadType = typeof(TestMessage).FullName,
            Payload = []
        };

        var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, new object[] { envelope }));
        var missingPayload = new OutboxEnvelope
        {
            Transport = "test",
            Operation = "orders.null",
            PayloadType = typeof(TestMessage).FullName,
            Payload = null
        };
        var missingException = Assert.Throws<TargetInvocationException>(() =>
            method.Invoke(null, new object[] { missingPayload }));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.IsType<InvalidOperationException>(missingException.InnerException);
    }

    private static ServiceProvider CreateProvider(
        bool fail = false,
        Action<SquirrelBoxOptions> configure = null,
        bool registerScheduler = false,
        bool registerPublisher = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TestOutboxPublisher(fail));
        if (registerPublisher)
            services.AddSingleton<IOutboxTransportPublisher>(provider => provider.GetRequiredService<TestOutboxPublisher>());
        if (registerScheduler)
        {
            services.AddSingleton<TestOutboxScheduler>();
            services.AddSingleton<IOutboxDeferredScheduler>(provider => provider.GetRequiredService<TestOutboxScheduler>());
        }
        services.AddSquirrelBox(configure).UseInMemory();
        return services.BuildServiceProvider();
    }

    private sealed record TestMessage(string Id);

    private sealed class InvalidOutboxProfile : IOutboxProfile
    {
        public bool CanHandle(OutboxEnqueueRequest request)
            => string.Equals(request.Transport, "invalid", StringComparison.OrdinalIgnoreCase);

        public OutboxEnvelope CreateEnvelope(OutboxEnqueueRequest request, OutboxProfileContext context)
            => new()
            {
                Id = Ulid.NewUlid(),
                Transport = request.Transport,
                Operation = request.Operation,
                Destination = request.Destination,
                PayloadType = typeof(TestMessage).FullName,
                Status = OutboxStatus.Pending,
                CreatedOnUtc = context.CreatedOnUtc,
                UpdatedOnUtc = context.CreatedOnUtc
            };
    }

    private sealed class TestOutboxPublisher : IOutboxTransportPublisher
    {
        private readonly bool _fail;

        public TestOutboxPublisher(bool fail)
        {
            _fail = fail;
        }

        public string Transport => "test";

        public OutboxPublishResult Result { get; set; } = OutboxPublishResult.Success;

        public IList<OutboxEnvelope> Published { get; } = [];

        public ValueTask<OutboxPublishResult> PublishAsync(
            OutboxEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            if (_fail)
                throw new InvalidOperationException("planned publisher failure");

            Published.Add(envelope);
            return ValueTask.FromResult(Result);
        }
    }

    private sealed class TestOutboxScheduler : IOutboxDeferredScheduler
    {
        public IList<OutboxEnvelope> Scheduled { get; } = [];

        public ValueTask ScheduleAsync(
            OutboxEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            Scheduled.Add(envelope);
            return ValueTask.CompletedTask;
        }
    }
}
