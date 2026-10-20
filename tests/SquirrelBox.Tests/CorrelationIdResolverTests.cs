using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;

namespace SquirrelBox.Tests;

public sealed class CorrelationIdResolverTests
{
    [Fact]
    public void Constructor_rejects_invalid_dependencies()
    {
        var factory = new RecordingFactory("generated");

        Assert.Throws<ArgumentNullException>(() => new DefaultCorrelationIdResolver(null, []));
        Assert.Throws<ArgumentNullException>(() => new DefaultCorrelationIdResolver(factory, null));
        Assert.Throws<ArgumentException>(() => new DefaultCorrelationIdResolver(
            factory,
            new ICorrelationIdResolutionStrategy[] { null }));
    }

    [Fact]
    public void Result_factories_expose_expected_states_and_validate_values()
    {
        Assert.Equal(CorrelationIdResolutionState.NotApplicable, CorrelationIdResolutionResult.NotApplicable.State);
        Assert.Equal(SquirrelBoxMetadataValueSource.Missing, CorrelationIdResolutionResult.NotApplicable.Source);
        Assert.Equal(CorrelationIdResolutionState.NotFound, CorrelationIdResolutionResult.NotFound.State);
        Assert.Null(CorrelationIdResolutionResult.NotFound.CorrelationId);
        Assert.Throws<ArgumentNullException>(() => CorrelationIdResolutionResult.Resolved(null));
        Assert.Throws<ArgumentException>(() => CorrelationIdResolutionResult.Resolved(" "));

        var resolved = CorrelationIdResolutionResult.Resolved("resolved");

        Assert.Equal(CorrelationIdResolutionState.Resolved, resolved.State);
        Assert.Equal("resolved", resolved.CorrelationId);
        Assert.Equal(SquirrelBoxMetadataValueSource.Resolved, resolved.Source);
        Assert.Null(resolved.ResolverName);
    }

    [Fact]
    public async Task Factory_only_uses_factory_without_calling_strategies()
    {
        var factory = new RecordingFactory("generated");
        var strategy = new DelegateStrategy("unused", 0, _ => CorrelationIdResolutionResult.Resolved("resolved"));
        var resolver = new DefaultCorrelationIdResolver(factory, [strategy]);

        var result = await resolver.ResolveAsync(Context(), CorrelationIdResolutionMode.FactoryOnly);

        Assert.Equal("generated", result.CorrelationId);
        Assert.Equal(SquirrelBoxMetadataValueSource.Generated, result.Source);
        Assert.Equal(nameof(RecordingFactory), result.ResolverName);
        Assert.Equal(1, factory.Calls);
        Assert.Equal(0, strategy.Calls);
    }

    [Fact]
    public async Task Incoming_value_has_precedence_and_remains_factory_compatible()
    {
        var factory = new RecordingFactory("generated");
        var strategy = new DelegateStrategy("unused", 0, _ => CorrelationIdResolutionResult.Resolved("resolved"));
        var resolver = new DefaultCorrelationIdResolver(factory, [strategy]);

        var result = await resolver.ResolveAsync(
            Context(incomingCorrelationId: "incoming"),
            CorrelationIdResolutionMode.ResolveRequired);

        Assert.Equal("incoming", result.CorrelationId);
        Assert.Equal(SquirrelBoxMetadataValueSource.Incoming, result.Source);
        Assert.Equal(1, factory.Calls);
        Assert.Equal(0, strategy.Calls);
    }

    [Fact]
    public async Task Strategies_run_by_order_then_registration_until_one_resolves()
    {
        var calls = new List<string>();
        var factory = new RecordingFactory("generated");
        var later = new DelegateStrategy("later", 20, _ =>
        {
            calls.Add("later");
            return CorrelationIdResolutionResult.Resolved("legacy-correlation");
        });
        var first = new DelegateStrategy("first", 10, _ =>
        {
            calls.Add("first");
            return CorrelationIdResolutionResult.NotApplicable;
        });
        var second = new DelegateStrategy("second", 10, _ =>
        {
            calls.Add("second");
            return CorrelationIdResolutionResult.NotFound;
        });
        var resolver = new DefaultCorrelationIdResolver(factory, [later, first, second]);

        var result = await resolver.ResolveAsync(Context(), CorrelationIdResolutionMode.ResolveThenGenerate);

        Assert.Equal(["first", "second", "later"], calls);
        Assert.Equal("legacy-correlation", result.CorrelationId);
        Assert.Equal("later", result.ResolverName);
        Assert.Equal(SquirrelBoxMetadataValueSource.Resolved, result.Source);
        Assert.Equal(0, factory.Calls);
    }

    [Fact]
    public async Task Blank_strategy_name_falls_back_to_strategy_type_name()
    {
        var strategy = new DelegateStrategy(" ", 0, _ => CorrelationIdResolutionResult.Resolved("resolved"));
        var resolver = new DefaultCorrelationIdResolver(new RecordingFactory("generated"), [strategy]);

        var result = await resolver.ResolveAsync(Context(), CorrelationIdResolutionMode.ResolveThenGenerate);

        Assert.Equal(nameof(DelegateStrategy), result.ResolverName);
    }

    [Fact]
    public async Task Resolve_then_generate_uses_factory_after_strategies_are_exhausted()
    {
        var factory = new RecordingFactory("fallback");
        var resolver = new DefaultCorrelationIdResolver(
            factory,
            [new DelegateStrategy("missing", 0, _ => CorrelationIdResolutionResult.NotFound)],
            NullLogger<DefaultCorrelationIdResolver>.Instance);

        var result = await resolver.ResolveAsync(Context(), CorrelationIdResolutionMode.ResolveThenGenerate);

        Assert.Equal("fallback", result.CorrelationId);
        Assert.Equal(SquirrelBoxMetadataValueSource.Generated, result.Source);
        Assert.Equal(1, factory.Calls);
    }

    [Fact]
    public async Task Resolve_required_returns_not_found_without_calling_factory()
    {
        var factory = new RecordingFactory("fallback");
        var resolver = new DefaultCorrelationIdResolver(factory, []);

        var result = await resolver.ResolveAsync(Context(), CorrelationIdResolutionMode.ResolveRequired);

        Assert.Same(CorrelationIdResolutionResult.NotFound, result);
        Assert.Equal(0, factory.Calls);
    }

    [Theory]
    [InlineData(CorrelationIdResolutionMode.Default)]
    [InlineData((CorrelationIdResolutionMode)999)]
    public async Task Resolver_rejects_non_concrete_modes(CorrelationIdResolutionMode mode)
    {
        var resolver = new DefaultCorrelationIdResolver(new RecordingFactory("generated"), []);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await resolver.ResolveAsync(Context(), mode));
    }

    [Fact]
    public async Task Resolver_honors_pre_cancelled_tokens()
    {
        var resolver = new DefaultCorrelationIdResolver(new RecordingFactory("generated"), []);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await resolver.ResolveAsync(Context(), CorrelationIdResolutionMode.FactoryOnly, cancellation.Token));
    }

    [Fact]
    public async Task Resolver_propagates_strategy_failures_and_cancellation()
    {
        var failure = new InvalidOperationException("database unavailable");
        var failing = new DefaultCorrelationIdResolver(
            new RecordingFactory("generated"),
            [new DelegateStrategy("failing", 0, _ => throw failure)]);
        var cancelled = new DefaultCorrelationIdResolver(
            new RecordingFactory("generated"),
            [new DelegateStrategy("cancelled", 0, _ => throw new OperationCanceledException())]);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await failing.ResolveAsync(Context(), CorrelationIdResolutionMode.ResolveThenGenerate));
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await cancelled.ResolveAsync(Context(), CorrelationIdResolutionMode.ResolveThenGenerate));

        Assert.Same(failure, actual);
    }

    [Fact]
    public async Task Resolver_rejects_null_and_unsupported_strategy_results()
    {
        var nullResolver = new DefaultCorrelationIdResolver(
            new RecordingFactory("generated"),
            [new DelegateStrategy("null", 0, _ => null)]);
        var constructor = typeof(CorrelationIdResolutionResult).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(CorrelationIdResolutionState), typeof(string), typeof(SquirrelBoxMetadataValueSource), typeof(string)],
            modifiers: null)!;
        var unsupportedResult = (CorrelationIdResolutionResult)constructor.Invoke(
            [(CorrelationIdResolutionState)999, null, SquirrelBoxMetadataValueSource.Missing, null]);
        var unsupportedResolver = new DefaultCorrelationIdResolver(
            new RecordingFactory("generated"),
            [new DelegateStrategy("unsupported", 0, _ => unsupportedResult)]);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await nullResolver.ResolveAsync(Context(), CorrelationIdResolutionMode.ResolveThenGenerate));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await unsupportedResolver.ResolveAsync(Context(), CorrelationIdResolutionMode.ResolveThenGenerate));
    }

    [Fact]
    public async Task Resolver_rejects_null_context_and_blank_factory_values()
    {
        var resolver = new DefaultCorrelationIdResolver(new RecordingFactory(" "), []);

        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await resolver.ResolveAsync(null, CorrelationIdResolutionMode.FactoryOnly));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await resolver.ResolveAsync(Context(), CorrelationIdResolutionMode.FactoryOnly));
    }

    private static InboxIdentityFactoryContext Context(string incomingCorrelationId = null)
        => new()
        {
            Source = "http",
            Operation = "POST /legacy",
            IdempotencyKey = "key",
            IncomingCorrelationId = incomingCorrelationId
        };

    private sealed class RecordingFactory(string value) : ICorrelationIdFactory
    {
        public int Calls { get; private set; }

        public string Create(InboxIdentityFactoryContext context)
        {
            Calls++;
            return context.IncomingCorrelationId ?? value;
        }
    }

    private sealed class DelegateStrategy(
        string name,
        int order,
        Func<InboxIdentityFactoryContext, CorrelationIdResolutionResult> resolve) : ICorrelationIdResolutionStrategy
    {
        public string Name { get; } = name;

        public int Order { get; } = order;

        public int Calls { get; private set; }

        public ValueTask<CorrelationIdResolutionResult> TryResolveAsync(
            InboxIdentityFactoryContext context,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(resolve(context));
        }
    }
}
