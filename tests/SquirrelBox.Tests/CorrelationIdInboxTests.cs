using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.InMemory;

namespace SquirrelBox.Tests;

public sealed class CorrelationIdInboxTests
{
    [Fact]
    public async Task Related_operations_share_resolved_correlation_but_keep_independent_attempt_identity()
    {
        using var provider = CreateProvider(options =>
            options.AddInboxPolicy("legacy-related", policy =>
                policy.CorrelationIdResolutionMode = CorrelationIdResolutionMode.ResolveRequired));
        string generatedCorrelationId;
        string firstTraceId;
        string firstAttemptId;

        using (var firstScope = provider.CreateScope())
        {
            var inbox = firstScope.ServiceProvider.GetRequiredService<IInboxService>();
            var first = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
                "http",
                "POST /legacy/a",
                "a-123",
                new LegacyRequest("123")));
            generatedCorrelationId = first.EffectiveCorrelationId;
            firstTraceId = first.EffectiveTraceId;
            firstAttemptId = first.EffectiveAttemptId;
            provider.GetRequiredService<LegacyCorrelationDatabase>()
                .Store("123", generatedCorrelationId);
            await inbox.CompleteCurrentAsync();

            Assert.Equal(SquirrelBoxMetadataValueSource.Generated, first.Entry.CorrelationIdSource);
            Assert.Equal(
                nameof(DefaultCorrelationIdFactory),
                first.Entry.Metadata[SquirrelBoxMetadataNames.CorrelationIdResolver]);
        }

        using (var secondScope = provider.CreateScope())
        {
            var inbox = secondScope.ServiceProvider.GetRequiredService<IInboxService>();
            var second = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
                "http",
                "POST /legacy/b",
                "b-123",
                new LegacyRequest("123"),
                policyName: "legacy-related"));

            Assert.Equal(InboxOpenState.Opened, second.State);
            Assert.Equal(generatedCorrelationId, second.EffectiveCorrelationId);
            Assert.NotEqual(firstTraceId, second.EffectiveTraceId);
            Assert.NotEqual(firstAttemptId, second.EffectiveAttemptId);
            Assert.Equal(SquirrelBoxMetadataValueSource.Resolved, second.Entry.CorrelationIdSource);
            Assert.Equal(
                LegacyCorrelationStrategy.StrategyName,
                second.Entry.Metadata[SquirrelBoxMetadataNames.CorrelationIdResolver]);
        }
    }

    [Fact]
    public async Task Resolve_required_rejects_without_reserving_an_inbox_entry()
    {
        using var provider = CreateProvider(options =>
            options.DefaultCorrelationIdResolutionMode = CorrelationIdResolutionMode.ResolveRequired);
        using var scope = provider.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        var diagnostics = scope.ServiceProvider.GetRequiredService<IInboxDiagnosticsStore>();

        var result = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            "http",
            "POST /legacy/b",
            "missing-123",
            new LegacyRequest("missing")));

        Assert.Equal(InboxOpenState.MissingCorrelationId, result.State);
        Assert.False(result.Accepted);
        Assert.Null(result.Entry);
        Assert.Empty(await diagnostics.QueryAsync(new InboxQuery()));
        Assert.Equal(
            InboxPolicyAction.Reject,
            scope.ServiceProvider.GetRequiredService<IInboxPolicyResolver>().Resolve(result).Action);
    }

    [Fact]
    public async Task Request_override_wins_over_named_policy_and_global_mode()
    {
        using var provider = CreateProvider(options =>
        {
            options.DefaultCorrelationIdResolutionMode = CorrelationIdResolutionMode.ResolveRequired;
            options.AddInboxPolicy("resolve", policy =>
                policy.CorrelationIdResolutionMode = CorrelationIdResolutionMode.ResolveThenGenerate);
        });
        using var scope = provider.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();

        var result = await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            "manual",
            "factory-override",
            "override-key",
            new LegacyRequest("missing"),
            policyName: "resolve",
            correlationIdResolutionMode: CorrelationIdResolutionMode.FactoryOnly));

        Assert.Equal(InboxOpenState.Opened, result.State);
        Assert.Equal(SquirrelBoxMetadataValueSource.Generated, result.Entry.CorrelationIdSource);
    }

    [Fact]
    public async Task Named_policy_mode_wins_over_global_mode_and_default_global_value_is_safe()
    {
        using var provider = CreateProvider(options =>
        {
            options.DefaultCorrelationIdResolutionMode = CorrelationIdResolutionMode.Default;
            options.AddInboxPolicy("required", policy =>
                policy.CorrelationIdResolutionMode = CorrelationIdResolutionMode.ResolveRequired);
        });
        using var firstScope = provider.CreateScope();

        var required = await firstScope.ServiceProvider.GetRequiredService<IInboxService>()
            .OpenOrContinueAsync(InboxOpenRequest.For(
                "manual",
                "required",
                "required-key",
                new LegacyRequest("missing"),
                policyName: "required"));

        Assert.Equal(InboxOpenState.MissingCorrelationId, required.State);

        using var secondScope = provider.CreateScope();
        var defaulted = await secondScope.ServiceProvider.GetRequiredService<IInboxService>()
            .OpenOrContinueAsync(InboxOpenRequest.For(
                "manual",
                "default",
                "default-key",
                new LegacyRequest("missing")));

        Assert.Equal(InboxOpenState.Opened, defaulted.State);
        Assert.Equal(SquirrelBoxMetadataValueSource.Generated, defaulted.Entry.CorrelationIdSource);
    }

    [Fact]
    public void Resolver_and_application_strategies_are_scoped()
    {
        using var provider = CreateProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.Same(
            first.ServiceProvider.GetRequiredService<ICorrelationIdResolver>(),
            first.ServiceProvider.GetRequiredService<ICorrelationIdResolver>());
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<ICorrelationIdResolver>(),
            second.ServiceProvider.GetRequiredService<ICorrelationIdResolver>());
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<ICorrelationIdResolutionStrategy>(),
            second.ServiceProvider.GetRequiredService<ICorrelationIdResolutionStrategy>());
    }

    private static ServiceProvider CreateProvider(Action<SquirrelBoxOptions> configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<LegacyCorrelationDatabase>();
        services.AddScoped<ICorrelationIdResolutionStrategy, LegacyCorrelationStrategy>();
        services.AddSquirrelBox(configure).UseInMemory();
        return services.BuildServiceProvider();
    }

    private sealed record LegacyRequest(string LegacyId);

    private sealed class LegacyCorrelationDatabase
    {
        private readonly ConcurrentDictionary<string, string> _correlations = new(StringComparer.Ordinal);

        public void Store(string legacyId, string correlationId)
            => _correlations[legacyId] = correlationId;

        public bool TryGet(string legacyId, out string correlationId)
            => _correlations.TryGetValue(legacyId, out correlationId);
    }

    private sealed class LegacyCorrelationStrategy(
        LegacyCorrelationDatabase database) : ICorrelationIdResolutionStrategy
    {
        public const string StrategyName = "legacy-database";

        public string Name => StrategyName;

        public int Order => 100;

        public async ValueTask<CorrelationIdResolutionResult> TryResolveAsync(
            InboxIdentityFactoryContext context,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            if (context.Payload is not LegacyRequest request)
                return CorrelationIdResolutionResult.NotApplicable;

            return database.TryGet(request.LegacyId, out var correlationId)
                ? CorrelationIdResolutionResult.Resolved(correlationId)
                : CorrelationIdResolutionResult.NotFound;
        }
    }
}
