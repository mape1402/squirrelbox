using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.InMemory;

namespace SquirrelBox.Messaging.Tests;

public sealed class CorrelationIdMessagingTests
{
    [Fact]
    public async Task Message_policy_resolves_related_correlation_and_enriches_transport_metadata()
    {
        using var provider = CreateProvider();
        var database = provider.GetRequiredService<LegacyMessageCorrelationDatabase>();
        database.Store("123", "correlation-abc");
        using var scope = provider.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IInboxMessageService>();

        var opened = await service.OpenAsync(new InboxMessageContext
        {
            Transport = "pigeon",
            Topic = "legacy-related",
            Version = "2",
            Subscription = "legacy-consumer",
            Operation = "LegacyRelated",
            Payload = new LegacyMessage("123")
        });
        var outgoingMetadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        service.AttachEffectiveKey(outgoingMetadata);
        SquirrelBoxMessageMetadata.TryReadFrom(outgoingMetadata, out var metadata);

        Assert.True(opened.Enabled);
        Assert.True(opened.Decision.ShouldExecute);
        Assert.Equal("correlation-abc", opened.EffectiveCorrelationId);
        Assert.Equal(SquirrelBoxMetadataValueSource.Resolved, opened.OpenResult.Entry.CorrelationIdSource);
        Assert.Equal("correlation-abc", metadata.CorrelationId);
        Assert.Equal(opened.EffectiveIdempotencyKey, metadata.IdempotencyKey);
        Assert.Equal(opened.EffectiveTraceId, metadata.TraceId);
        Assert.Equal(opened.EffectiveAttemptId, metadata.AttemptId);
    }

    [Fact]
    public async Task Message_policy_rejects_when_required_correlation_cannot_be_resolved()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var opened = await scope.ServiceProvider.GetRequiredService<IInboxMessageService>()
            .OpenAsync(new InboxMessageContext
            {
                Transport = "pigeon",
                Topic = "legacy-related",
                Version = "2",
                Subscription = "legacy-consumer",
                Operation = "LegacyRelated",
                Payload = new LegacyMessage("missing")
            });

        Assert.Equal(InboxOpenState.MissingCorrelationId, opened.OpenResult.State);
        Assert.Equal(InboxPolicyAction.Reject, opened.Decision.Action);
        Assert.False(opened.Decision.ShouldExecute);
    }

    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<LegacyMessageCorrelationDatabase>();
        services.AddScoped<ICorrelationIdResolutionStrategy, LegacyMessageCorrelationStrategy>();
        services.AddSquirrelBox().UseInMemory();
        services.AddSquirrelBoxMessaging(options =>
            options.ScanAssemblyContaining<CorrelationIdMessagingTests>());
        return services.BuildServiceProvider();
    }
}

public sealed class CorrelationIdMessagePolicyProfile : InboxMessagePolicyProfile
{
    public override void Configure(InboxMessagePolicyProfileBuilder builder)
        => builder
            .Match(
                topic: "legacy-related",
                version: "2",
                subscription: "legacy-consumer",
                operation: "LegacyRelated")
            .WithCorrelationIdResolution(CorrelationIdResolutionMode.ResolveRequired);
}

public sealed record LegacyMessage(string LegacyId);

public sealed class LegacyMessageCorrelationDatabase
{
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

    public void Store(string legacyId, string correlationId)
        => _values[legacyId] = correlationId;

    public bool TryGet(string legacyId, out string correlationId)
        => _values.TryGetValue(legacyId, out correlationId);
}

public sealed class LegacyMessageCorrelationStrategy(
    LegacyMessageCorrelationDatabase database) : ICorrelationIdResolutionStrategy
{
    public string Name => "legacy-message-database";

    public int Order => 0;

    public ValueTask<CorrelationIdResolutionResult> TryResolveAsync(
        InboxIdentityFactoryContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Payload is not LegacyMessage message)
            return ValueTask.FromResult(CorrelationIdResolutionResult.NotApplicable);

        return ValueTask.FromResult(
            database.TryGet(message.LegacyId, out var correlationId)
                ? CorrelationIdResolutionResult.Resolved(correlationId)
                : CorrelationIdResolutionResult.NotFound);
    }
}
