using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Mule;
using NSubstitute;
using Ulid = System.Ulid;

namespace SquirrelBox.Mule.Tests;

public sealed class MuleBranchCoverageTests
{
    [Fact]
    public void Mule_public_components_require_dependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new SquirrelBoxOperationMuleAction(null, Substitute.For<ISquirrelBoxOperationService>()));
        Assert.Throws<ArgumentNullException>(() => new SquirrelBoxOperationMuleAction(Substitute.For<IInboxService>(), null));
        Assert.Throws<ArgumentNullException>(() => new SquirrelBoxOperationMuleScheduler(null));
        Assert.Throws<ArgumentNullException>(() => new SquirrelBoxOutboxMuleAction(null));
        Assert.Throws<ArgumentNullException>(() => new SquirrelBoxOutboxMuleScheduler(null));
        Assert.Throws<ArgumentNullException>(() => new TestMuleAction(null));
    }

    [Fact]
    public void Mule_action_context_requires_both_contexts()
    {
        var entry = new InboxContext(new InboxEntry(), "test", true);
        var mule = CreateContext(action: new DurableAction());

        Assert.Throws<ArgumentNullException>(() => new SquirrelBoxMuleActionContext<string>(null, mule));
        Assert.Throws<ArgumentNullException>(() => new SquirrelBoxMuleActionContext<string>(entry, null));
        Assert.Equal("payload", new SquirrelBoxMuleActionContext<string>(entry, mule).Payload);
    }

    [Fact]
    public void Mule_action_uses_default_owner_for_blank_value()
    {
        var action = new TestMuleAction(Substitute.For<IInboxService>(), " ");

        Assert.NotNull(action);
    }

    [Fact]
    public void Terminal_attempt_resolution_handles_missing_action_and_invalid_limits()
    {
        var method = typeof(SquirrelBoxMuleAction<string>).GetMethod(
            "IsTerminalAttempt",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var firstAttempt = CreateContext(action: new DurableAction(), metadata: new Dictionary<string, string>
        {
            [SquirrelBoxMuleMetadata.DeferredMaxAttempts] = "2"
        });
        var invalid = CreateContext(action: new DurableAction(), metadata: new Dictionary<string, string>
        {
            [SquirrelBoxMuleMetadata.DeferredMaxAttempts] = "invalid"
        });
        var one = CreateContext(action: new DurableAction(), metadata: new Dictionary<string, string>
        {
            [SquirrelBoxMuleMetadata.DeferredMaxAttempts] = "1"
        });

        Assert.False((bool)method.Invoke(null, [firstAttempt])!);
        Assert.True((bool)method.Invoke(null, [invalid])!);
        Assert.True((bool)method.Invoke(null, [one])!);
    }

    [Fact]
    public void Inbox_scheduler_private_resolution_handles_empty_deferred_configuration()
    {
        var scheduler = new InboxMuleScheduler(
            Substitute.For<IInboxService>(),
            Substitute.For<IMuleClient>(),
            Options.Create(new MuleSettings()));
        var apply = typeof(InboxMuleScheduler).GetMethod(
            "ApplyDeferredOptions",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var write = typeof(InboxMuleScheduler).GetMethod(
            "WriteSquirrelBoxMetadata",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var entry = new InboxEntry
        {
            Id = Ulid.NewUlid(),
            IdempotencyKey = "key",
            CorrelationId = "correlation",
            Deferred = null
        };
        var context = new InboxContext(entry, "test", true);
        var options = new EnqueueOptions { Lane = MuleSettings.DefaultLane };

        apply.Invoke(scheduler, [options, entry]);
        write.Invoke(scheduler, [options, context]);

        Assert.Equal(MuleSettings.DefaultLane, options.Lane);
        Assert.False(options.Metadata.ContainsKey(SquirrelBoxMuleMetadata.DeferredPolicyName));
        Assert.False(options.Metadata.ContainsKey(SquirrelBoxMuleMetadata.DeferredRetryDelayMilliseconds));
        Assert.False(options.Metadata.ContainsKey(SquirrelBoxMuleMetadata.DeferredRetryMaxDelayMilliseconds));
        Assert.False(options.Metadata.ContainsKey(SquirrelBoxMuleMetadata.DeferredRetryBackoff));
        Assert.False(options.Metadata.ContainsKey(SquirrelBoxMuleMetadata.DeferredRetryJitterRatio));
    }

    [Fact]
    public void Inbox_scheduler_preserves_custom_lane_and_resolves_missing_and_legacy_lanes()
    {
        var settings = new MuleSettings();
        settings.Lanes["legacy"] = new MuleLaneSettings
        {
            RetryPolicy = null,
            MaxAttempts = 3
        };
        settings.Lanes["zero"] = new MuleLaneSettings
        {
            RetryPolicy = null,
            MaxAttempts = 0
        };
        var scheduler = new InboxMuleScheduler(
            Substitute.For<IInboxService>(),
            Substitute.For<IMuleClient>(),
            Options.Create(settings));
        var apply = typeof(InboxMuleScheduler).GetMethod(
            "ApplyDeferredOptions",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var resolve = typeof(InboxMuleScheduler).GetMethod(
            "ResolveMaxAttempts",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var entry = new InboxEntry
        {
            Deferred = new InboxDeferredExecutionOptions { Lane = "deferred" }
        };
        var options = new EnqueueOptions { Lane = "custom" };
        var blankOptions = new EnqueueOptions { Lane = null };

        apply.Invoke(scheduler, [options, entry]);
        apply.Invoke(scheduler, [blankOptions, entry]);

        Assert.Equal("custom", options.Lane);
        Assert.Equal("deferred", blankOptions.Lane);
        Assert.Equal(settings.MaxAttempts, resolve.Invoke(scheduler, [null, null]));
        Assert.Equal(settings.MaxAttempts, resolve.Invoke(scheduler, ["missing", null]));
        Assert.Equal(3, resolve.Invoke(scheduler, ["legacy", null]));
        Assert.Equal(settings.MaxAttempts, resolve.Invoke(scheduler, ["zero", null]));
    }

    [Fact]
    public void Mule_settings_configurator_rejects_null_option_value_and_settings()
    {
        var options = Substitute.For<IOptions<SquirrelBoxOptions>>();
        options.Value.Returns((SquirrelBoxOptions)null);
        var type = typeof(InboxMuleScheduler).Assembly.GetType(
            "SquirrelBox.Mule.SquirrelBoxMuleSettingsConfigurator",
            throwOnError: true)!;
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        var nullOptions = Assert.Throws<TargetInvocationException>(() => constructor.Invoke([(object)null]));
        var nullValue = Assert.Throws<TargetInvocationException>(() => constructor.Invoke([options]));
        var instance = constructor.Invoke([Options.Create(new SquirrelBoxOptions())]);
        var configure = type.GetMethod("Configure")!;
        var nullSettings = Assert.Throws<TargetInvocationException>(() => configure.Invoke(instance, [null]));

        Assert.IsType<ArgumentNullException>(nullOptions.InnerException);
        Assert.IsType<ArgumentNullException>(nullValue.InnerException);
        Assert.IsType<ArgumentNullException>(nullSettings.InnerException);
    }

    [Fact]
    public void Mule_settings_configurator_covers_null_policy_and_retry_fallbacks()
    {
        var type = typeof(InboxMuleScheduler).Assembly.GetType(
            "SquirrelBox.Mule.SquirrelBoxMuleSettingsConfigurator",
            throwOnError: true)!;
        var configurePolicy = type.GetMethod("ConfigurePolicy", BindingFlags.Static | BindingFlags.NonPublic)!;
        var firstPositive = type.GetMethod("FirstPositive", BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(typeof(int));
        var settings = new MuleSettings
        {
            RetryPolicy = new MuleRetryPolicy
            {
                MaxAttempts = 7,
                Delay = TimeSpan.FromSeconds(3),
                MaxDelay = TimeSpan.FromSeconds(9),
                Backoff = MuleRetryBackoff.Linear,
                JitterRatio = 0.4
            }
        };
        var policy = new InboxIdempotencyPolicy { Name = "global-fallbacks" };
        policy.Deferred.MaxAttempts = 2;

        configurePolicy.Invoke(null, [settings, null]);
        configurePolicy.Invoke(null, [settings, new InboxIdempotencyPolicy()]);
        configurePolicy.Invoke(null, [settings, policy]);

        var lane = settings.Lanes["squirrelbox.global-fallbacks"];
        Assert.Equal(TimeSpan.FromSeconds(3), lane.RetryPolicy.Delay);
        Assert.Equal(TimeSpan.FromSeconds(9), lane.RetryPolicy.MaxDelay);
        Assert.Equal(0.4, lane.RetryPolicy.JitterRatio);
        Assert.Equal(1, firstPositive.Invoke(null, [1, 2]));
        Assert.Equal(2, firstPositive.Invoke(null, [0, 2]));
        Assert.Equal(0, firstPositive.Invoke(null, [0, 0]));
    }

    [Fact]
    public void Mule_settings_configurator_handles_all_null_retry_fallbacks()
    {
        var type = typeof(InboxMuleScheduler).Assembly.GetType(
            "SquirrelBox.Mule.SquirrelBoxMuleSettingsConfigurator",
            throwOnError: true)!;
        var configurePolicy = type.GetMethod("ConfigurePolicy", BindingFlags.Static | BindingFlags.NonPublic)!;
        var settings = new MuleSettings { RetryPolicy = null };
        settings.Lanes["squirrelbox.null-fallbacks"] = new MuleLaneSettings
        {
            RetryPolicy = null,
            MaxAttempts = 0
        };
        var policy = new InboxIdempotencyPolicy { Name = "null-fallbacks" };
        policy.Deferred.JitterRatio = 0.1;

        configurePolicy.Invoke(null, [settings, policy]);

        var retry = settings.Lanes["squirrelbox.null-fallbacks"].RetryPolicy;
        Assert.Null(retry.MaxDelay);
        Assert.Equal(settings.RetryDelay, retry.Delay);
        Assert.Equal(MuleRetryBackoff.Fixed, retry.Backoff);
        Assert.Equal(0.1, retry.JitterRatio);
    }

    [Fact]
    public void Mule_settings_configurator_preserves_existing_lane_jitter()
    {
        var type = typeof(InboxMuleScheduler).Assembly.GetType(
            "SquirrelBox.Mule.SquirrelBoxMuleSettingsConfigurator",
            throwOnError: true)!;
        var configurePolicy = type.GetMethod("ConfigurePolicy", BindingFlags.Static | BindingFlags.NonPublic)!;
        var settings = new MuleSettings();
        settings.Lanes["squirrelbox.existing-jitter"] = new MuleLaneSettings
        {
            RetryPolicy = new MuleRetryPolicy { JitterRatio = 0.25 }
        };
        var policy = new InboxIdempotencyPolicy { Name = "existing-jitter" };
        policy.Deferred.MaxAttempts = 2;
        policy.Deferred.MaxDelay = TimeSpan.FromSeconds(12);

        configurePolicy.Invoke(null, [settings, policy]);

        var retry = settings.Lanes["squirrelbox.existing-jitter"].RetryPolicy;
        Assert.Equal(TimeSpan.FromSeconds(12), retry.MaxDelay);
        Assert.Equal(0.25, retry.JitterRatio);
    }

    [Fact]
    public async Task Operation_action_creates_empty_completion_before_result_exists()
    {
        var action = new SquirrelBoxOperationMuleAction(
            Substitute.For<IInboxService>(),
            Substitute.For<ISquirrelBoxOperationService>());
        var method = typeof(SquirrelBoxOperationMuleAction).GetMethod(
            "CreateCompletionAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;

        var pending = (ValueTask<InboxCompletion>)method.Invoke(
            action,
            [null, CancellationToken.None])!;

        Assert.Same(InboxCompletion.Empty, await pending);
    }

    private static MuleActionContext<string> CreateContext(
        DurableAction action,
        IReadOnlyDictionary<string, string> metadata = null)
    {
        if (action is not null)
            action.Metadata = System.Text.Json.JsonSerializer.Serialize(metadata ?? new Dictionary<string, string>());

        return new MuleActionContext<string>(
            action,
            new ServiceCollection().BuildServiceProvider(),
            "payload");
    }

    private sealed class TestMuleAction : SquirrelBoxMuleAction<string>
    {
        public TestMuleAction(IInboxService inbox, string owner = "mule")
            : base(inbox, owner)
        {
        }

        protected override ValueTask ExecuteInboxAsync(
            SquirrelBoxMuleActionContext<string> context,
            CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }
}
