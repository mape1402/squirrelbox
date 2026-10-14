using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using SquirrelBox.InMemory;

namespace SquirrelBox.Messaging.Tests;

public sealed class InboxMessageServiceE2ETests
{
    [Fact]
    public async Task OpenAsync_does_not_open_inbox_when_no_policy_profile_matches()
    {
        var provider = CreateProvider(scanProfiles: false);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInboxMessageService>();

        var opened = await service.OpenAsync(new InboxMessageContext
        {
            Transport = "rabbitmq",
            Topic = "orders",
            Version = "v1",
            Subscription = "billing",
            Operation = "created",
            Payload = new OrderMessage("order-disabled"),
            Metadata = CreateSquirrelBoxMetadata("message-key")
        });

        Assert.False(opened.Enabled);
        Assert.True(opened.ShouldExecute);
        Assert.Null(scope.ServiceProvider.GetRequiredService<IInboxService>().Current);
    }

    [Fact]
    public async Task OpenAsync_uses_structured_metadata_and_attaches_section_to_reply_metadata()
    {
        var provider = CreateProvider();

        using var firstScope = provider.CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<IInboxMessageService>();
        var opened = await first.OpenAsync(new InboxMessageContext
        {
            Transport = "rabbitmq",
            Topic = "orders",
            Version = "v1",
            Subscription = "billing",
            Operation = "created",
            Payload = new OrderMessage("order-1"),
            Metadata = CreateSquirrelBoxMetadata("message-key")
        });

        var replyMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        first.AttachEffectiveKey(replyMetadata);
        var openedCorrelationId = opened.EffectiveCorrelationId;
        var openedAttemptId = opened.EffectiveAttemptId;
        var openedTraceId = opened.EffectiveTraceId;
        await firstScope.ServiceProvider.GetRequiredService<IInboxService>().CompleteCurrentAsync();

        using var secondScope = provider.CreateScope();
        var duplicate = await secondScope.ServiceProvider
            .GetRequiredService<IInboxMessageService>()
            .OpenAsync(new InboxMessageContext
            {
                Transport = "rabbitmq",
                Topic = "orders",
                Version = "v1",
                Subscription = "billing",
                Operation = "created",
                Payload = new OrderMessage("order-1"),
                Metadata = CreateSquirrelBoxMetadata("message-key")
            });

        Assert.True(opened.ShouldExecute);
        var reply = ReadSquirrelBoxMetadata(replyMetadata);
        Assert.Equal("message-key", reply.IdempotencyKey);
        Assert.Equal(openedCorrelationId, reply.CorrelationId);
        Assert.Equal(openedAttemptId, reply.AttemptId);
        Assert.Equal(openedTraceId, reply.TraceId);
        AssertNoFlatIdentityMetadata(replyMetadata);
        Assert.Equal(InboxOpenState.DuplicateCompleted, duplicate.OpenResult.State);
        Assert.Equal("orders:v1/billing/created", duplicate.OpenResult.Entry.Operation);
        Assert.Equal(openedCorrelationId, duplicate.EffectiveCorrelationId);
        Assert.NotEqual(openedAttemptId, duplicate.EffectiveAttemptId);
        Assert.NotEqual(openedTraceId, duplicate.EffectiveTraceId);
    }

    [Fact]
    public async Task OpenAsync_computes_payload_key_when_metadata_and_message_id_are_missing()
    {
        var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInboxMessageService>();

        var opened = await service.OpenAsync(new InboxMessageContext
        {
            Transport = "rabbitmq",
            Topic = "orders",
            Version = "v2",
            Subscription = "shipping",
            Operation = "created",
            Payload = new OrderMessage("order-2")
        });

        var replyMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        service.AttachEffectiveKey(replyMetadata);

        Assert.Equal(InboxOpenState.Opened, opened.OpenResult.State);
        Assert.Equal(InboxIdempotencyKeySource.ComputedFromPayload, opened.OpenResult.IdempotencyKeySource);
        var reply = ReadSquirrelBoxMetadata(replyMetadata);
        Assert.Equal(opened.EffectiveIdempotencyKey, reply.IdempotencyKey);
        Assert.Equal(opened.EffectiveCorrelationId, reply.CorrelationId);
        Assert.Equal(opened.EffectiveAttemptId, reply.AttemptId);
        Assert.Equal(opened.EffectiveTraceId, reply.TraceId);
        AssertNoFlatIdentityMetadata(replyMetadata);
    }

    [Fact]
    public async Task OpenAsync_preserves_structured_metadata_values_for_identity_propagation()
    {
        var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInboxMessageService>();

        var opened = await service.OpenAsync(new InboxMessageContext
        {
            Transport = "rabbitmq",
            Topic = "orders",
            Version = "v1",
            Subscription = "billing",
            Operation = "created",
            Payload = new OrderMessage("order-custom"),
            Metadata = CreateSquirrelBoxMetadata(
                idempotencyKey: "custom-key",
                correlationId: "corr-custom",
                traceId: "trace-custom")
        });

        var replyMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        service.AttachEffectiveKey(replyMetadata);
        var reply = ReadSquirrelBoxMetadata(replyMetadata);

        Assert.Equal("custom-key", opened.EffectiveMetadata.IdempotencyKey);
        Assert.Equal("corr-custom", opened.EffectiveMetadata.CorrelationId);
        Assert.Equal("trace-custom", opened.EffectiveMetadata.TraceId);
        Assert.Equal("custom-key", reply.IdempotencyKey);
        Assert.Equal("corr-custom", reply.CorrelationId);
        Assert.Equal("trace-custom", reply.TraceId);
        Assert.Equal(opened.EffectiveAttemptId, reply.AttemptId);
        AssertNoFlatIdentityMetadata(replyMetadata);
    }

    [Fact]
    public async Task OpenAsync_applies_deferred_settings_from_matching_message_policy()
    {
        var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInboxMessageService>();

        var opened = await service.OpenAsync(new InboxMessageContext
        {
            Transport = "rabbitmq",
            Topic = "deferred-orders",
            Version = "v1",
            Subscription = "workers",
            Operation = "created",
            Payload = new OrderMessage("order-deferred")
        });

        Assert.Equal(InboxExecutionMode.Deferred, opened.OpenResult.Entry.ExecutionMode);
        Assert.Equal("messaging-retry", opened.OpenResult.Entry.Deferred.Lane);
        Assert.Equal(3, opened.OpenResult.Entry.Deferred.MaxAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(250), opened.OpenResult.Entry.Deferred.Delay);
        Assert.Equal(InboxRetryBackoff.Linear, opened.OpenResult.Entry.Deferred.Backoff);
        Assert.Equal(TimeSpan.FromMinutes(2), opened.OpenResult.Entry.Deferred.InProgressTimeout);
    }

    [Fact]
    public async Task OpenAsync_ignores_flat_identity_metadata_when_structured_section_is_missing()
    {
        var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInboxMessageService>();

        var opened = await service.OpenAsync(new InboxMessageContext
        {
            Transport = "rabbitmq",
            Topic = "orders",
            Version = "v1",
            Subscription = "billing",
            Operation = "created",
            Payload = new OrderMessage("order-flat"),
            Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["idempotency-key"] = "flat-key",
                ["correlation-id"] = "flat-correlation",
                ["trace-id"] = "flat-trace"
            }
        });

        var replyMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        service.AttachEffectiveKey(replyMetadata);
        var reply = ReadSquirrelBoxMetadata(replyMetadata);

        Assert.Equal(InboxIdempotencyKeySource.ComputedFromPayload, opened.OpenResult.IdempotencyKeySource);
        Assert.NotEqual("flat-key", opened.EffectiveIdempotencyKey);
        Assert.NotEqual("flat-correlation", opened.EffectiveCorrelationId);
        Assert.NotEqual("flat-trace", opened.EffectiveTraceId);
        Assert.Equal(opened.EffectiveIdempotencyKey, reply.IdempotencyKey);
        AssertNoFlatIdentityMetadata(replyMetadata);
    }

    [Fact]
    public async Task OpenAsync_logs_warning_when_message_is_rejected_for_missing_idempotency_key()
    {
        var logger = new ListLogger<DefaultInboxMessageService>();
        var provider = CreateProvider(logger: logger);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInboxMessageService>();

        var opened = await service.OpenAsync(new InboxMessageContext
        {
            Transport = "rabbitmq",
            Topic = "orders",
            Version = "v1",
            Subscription = "billing",
            Operation = "created"
        });

        Assert.False(opened.ShouldExecute);
        Assert.Equal(InboxOpenState.MissingIdempotencyKey, opened.OpenResult.State);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.EventId == SquirrelBoxLogEvents.MessagingMetadataMissing);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            entry.EventId == SquirrelBoxLogEvents.InboxMissingIdempotencyKey &&
            entry.Message.Contains("State=MissingIdempotencyKey", StringComparison.Ordinal));
    }

    [Fact]
    public void AttachEffectiveKey_does_nothing_when_no_message_has_opened()
    {
        var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInboxMessageService>();
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        service.AttachEffectiveKey(metadata);

        Assert.Empty(metadata);
    }

    [Fact]
    public async Task OpenAsync_can_use_correlation_id_when_key_metadata_is_missing()
    {
        var provider = CreateProvider(configureMessaging: options => options.UseCorrelationIdWhenKeyIsMissing = true);
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInboxMessageService>();

        var opened = await service.OpenAsync(new InboxMessageContext
        {
            Transport = "rabbitmq",
            Topic = "orders",
            Version = "v1",
            Subscription = "billing",
            Operation = "created",
            CorrelationId = "corr-as-key",
            Payload = new OrderMessage("order-correlation")
        });

        Assert.Equal("corr-as-key", opened.EffectiveIdempotencyKey);
        Assert.Equal("corr-as-key", opened.EffectiveCorrelationId);
    }

    [Fact]
    public async Task OpenAsync_uses_message_id_with_default_name_when_metadata_key_is_missing()
    {
        var provider = CreateProvider(configureMessaging: options => options.MessageIdMetadataName = " ");
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInboxMessageService>();

        var opened = await service.OpenAsync(new InboxMessageContext
        {
            Transport = "rabbitmq",
            Topic = "orders",
            Version = "v1",
            Subscription = "billing",
            Operation = "created",
            MessageId = "message-id-key",
            Payload = new OrderMessage("order-message-id")
        });

        Assert.Equal("message-id-key", opened.EffectiveIdempotencyKey);
        Assert.Equal("message-id", opened.OpenResult.Entry.IdempotencyKeyName);
    }

    [Fact]
    public async Task OpenAsync_uses_correlation_when_message_id_fallback_is_disabled_and_metadata_is_empty()
    {
        var provider = CreateProvider(configureMessaging: options =>
        {
            options.UseMessageIdWhenKeyIsMissing = false;
            options.UseCorrelationIdWhenKeyIsMissing = true;
        });
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInboxMessageService>();

        var opened = await service.OpenAsync(new InboxMessageContext
        {
            Transport = "rabbitmq",
            Topic = "orders",
            Version = "v1",
            Subscription = "billing",
            Operation = "created",
            MessageId = "ignored-message-id",
            CorrelationId = "correlation-key",
            Payload = new OrderMessage("order-correlation-fallback"),
            Metadata = new Dictionary<string, string>
            {
                [SquirrelBoxMetadataNames.MetadataSection] = "{}"
            }
        });

        Assert.Equal("correlation-key", opened.EffectiveIdempotencyKey);
    }

    [Fact]
    public async Task OpenAsync_uses_context_trace_and_default_operation_parts_when_optional_values_are_blank()
    {
        var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInboxMessageService>();

        var opened = await service.OpenAsync(new InboxMessageContext
        {
            Transport = "rabbitmq",
            Topic = "orders",
            Version = "",
            Subscription = null,
            Operation = " ",
            MessageId = "blank-operation-key",
            CorrelationId = "context-correlation",
            TraceId = "context-trace",
            Payload = new OrderMessage("order-blank-operation")
        });

        Assert.Equal("orders:unversioned/default/message", opened.OpenResult.Entry.Operation);
        Assert.Equal("context-correlation", opened.EffectiveCorrelationId);
        Assert.Equal("context-trace", opened.EffectiveTraceId);
        Assert.Equal("orders", opened.OpenResult.Entry.Metadata["topic"]);
        Assert.False(opened.OpenResult.Entry.Metadata.ContainsKey("version"));
    }

    [Fact]
    public void Default_operation_resolver_uses_unknown_topic_when_topic_is_blank()
    {
        var operation = new SquirrelBoxMessagingOptions().OperationResolver(new InboxMessageContext
        {
            Topic = " ",
            Version = " ",
            Subscription = " ",
            Operation = " "
        });

        Assert.Equal("unknown-topic:unversioned/default/message", operation);
    }

    [Fact]
    public async Task OpenAsync_accepts_null_transport_metadata_and_null_payload()
    {
        var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var opened = await scope.ServiceProvider.GetRequiredService<IInboxMessageService>()
            .OpenAsync(new InboxMessageContext
            {
                Transport = "rabbitmq",
                Topic = "orders",
                MessageId = "null-metadata-key",
                Metadata = null,
                Payload = null
            });

        Assert.True(opened.ShouldExecute);
        Assert.Equal("null-metadata-key", opened.EffectiveIdempotencyKey);
        Assert.Null(opened.OpenResult.Entry.PayloadType);
    }

    [Fact]
    public async Task OpenAsync_uses_global_execution_mode_resolver_as_last_fallback()
    {
        var provider = CreateProvider(configureMessaging: options =>
            options.ExecutionModeResolver = _ => InboxExecutionMode.Deferred);
        using var scope = provider.CreateScope();

        var opened = await scope.ServiceProvider.GetRequiredService<IInboxMessageService>()
            .OpenAsync(new InboxMessageContext
            {
                Transport = "rabbitmq",
                Topic = "orders",
                MessageId = "global-mode",
                Payload = new OrderMessage("global-mode")
            });

        Assert.Equal(InboxExecutionMode.Deferred, opened.OpenResult.Entry.ExecutionMode);
    }

    [Fact]
    public void Metadata_resolvers_accept_a_null_structured_model()
    {
        var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var service = Assert.IsType<DefaultInboxMessageService>(
            scope.ServiceProvider.GetRequiredService<IInboxMessageService>());
        var context = new InboxMessageContext
        {
            Transport = "rabbitmq",
            MessageId = "message",
            CorrelationId = "correlation",
            TraceId = "trace"
        };

        AssertResolvedValue(service, "ResolveIdempotencyKey", context, "message");
        AssertResolvedValue(service, "ResolveCorrelationId", context, "correlation");
        AssertResolvedValue(service, "ResolveTraceId", context, "trace");
    }

    private static void AssertResolvedValue(
        DefaultInboxMessageService service,
        string methodName,
        InboxMessageContext context,
        string expected)
    {
        var method = typeof(DefaultInboxMessageService).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var resolved = method.Invoke(service, [context, null])!;
        var value = resolved.GetType().GetProperty("Value")!.GetValue(resolved);

        Assert.Equal(expected, value);
    }

    private static ServiceProvider CreateProvider(
        bool scanProfiles = true,
        ListLogger<DefaultInboxMessageService> logger = null,
        Action<SquirrelBoxMessagingOptions> configureMessaging = null)
    {
        var services = new ServiceCollection();
        services.AddSquirrelBox().UseInMemory();
        services.AddSquirrelBoxMessaging(options =>
        {
            if (scanProfiles)
                options.ScanAssemblyContaining<InboxMessageServiceE2ETests>();

            configureMessaging?.Invoke(options);
        });

        if (logger is not null)
            services.AddSingleton<ILogger<DefaultInboxMessageService>>(logger);

        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string> CreateSquirrelBoxMetadata(
        string idempotencyKey,
        string correlationId = null,
        string traceId = null,
        string attemptId = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        new SquirrelBoxMessageMetadata
        {
            Metadata = new SquirrelBoxMetadata
            {
                IdempotencyKey = idempotencyKey,
                CorrelationId = correlationId,
                TraceId = traceId,
                AttemptId = attemptId
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

    private sealed record OrderMessage(string Id);

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, EventId EventId, string Message);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}

public sealed class InboxMessageServiceTestPolicyProfile : InboxMessagePolicyProfile
{
    public override void Configure(InboxMessagePolicyProfileBuilder builder)
    {
        builder.ForTopic("orders");
        builder.Match(topic: "deferred-orders", version: "v1", subscription: "workers", operation: "created")
            .DeferExecution()
            .WithDeferred(options =>
            {
                options.Lane = "messaging-retry";
                options.MaxAttempts = 3;
                options.Delay = TimeSpan.FromMilliseconds(250);
                options.Backoff = InboxRetryBackoff.Linear;
                options.InProgressTimeout = TimeSpan.FromMinutes(2);
            });
    }
}
