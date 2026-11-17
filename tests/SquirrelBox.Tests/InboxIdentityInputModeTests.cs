using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.InMemory;

namespace SquirrelBox.Tests;

public sealed class InboxIdentityInputModeTests
{
    [Fact]
    public async Task Default_modes_preserve_incoming_identity()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var opened = await OpenAsync(
            scope,
            idempotencyKey: "incoming-key",
            correlationId: "incoming-correlation");

        Assert.Equal("incoming-key", opened.EffectiveIdempotencyKey);
        Assert.Equal(InboxIdempotencyKeySource.Explicit, opened.IdempotencyKeySource);
        Assert.Equal("incoming-correlation", opened.EffectiveCorrelationId);
        Assert.Equal(SquirrelBoxMetadataValueSource.Incoming, opened.Entry.CorrelationIdSource);
    }

    [Fact]
    public async Task Request_modes_recompute_key_and_ignore_incoming_correlation()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var opened = await OpenAsync(
            scope,
            idempotencyKey: "ignored-key",
            correlationId: "ignored-correlation",
            allowPayloadHash: false,
            idempotencyMode: InboxIdempotencyKeyResolutionMode.RecomputeFromPayload,
            correlationInputMode: InboxCorrelationIdInputMode.IgnoreIncoming);

        Assert.NotEqual("ignored-key", opened.EffectiveIdempotencyKey);
        Assert.Equal(opened.Entry.PayloadHash, opened.EffectiveIdempotencyKey);
        Assert.Equal(InboxIdempotencyKeySource.ComputedFromPayload, opened.IdempotencyKeySource);
        Assert.NotEqual("ignored-correlation", opened.EffectiveCorrelationId);
        Assert.Equal(SquirrelBoxMetadataValueSource.Generated, opened.Entry.CorrelationIdSource);
    }

    [Fact]
    public async Task Named_policy_modes_apply_when_request_uses_defaults()
    {
        using var provider = CreateProvider(options =>
            options.AddInboxPolicy("canonical", policy =>
            {
                policy.IdempotencyKeyResolutionMode = InboxIdempotencyKeyResolutionMode.RecomputeFromPayload;
                policy.CorrelationIdInputMode = InboxCorrelationIdInputMode.IgnoreIncoming;
            }));
        using var scope = provider.CreateScope();

        var opened = await OpenAsync(
            scope,
            idempotencyKey: "ignored-key",
            correlationId: "ignored-correlation",
            policyName: "canonical");

        Assert.Equal(opened.Entry.PayloadHash, opened.EffectiveIdempotencyKey);
        Assert.NotEqual("ignored-correlation", opened.EffectiveCorrelationId);
    }

    [Fact]
    public async Task Global_modes_apply_when_entrypoint_and_policy_use_defaults()
    {
        using var provider = CreateProvider(options =>
        {
            options.DefaultIdempotencyKeyResolutionMode = InboxIdempotencyKeyResolutionMode.RecomputeFromPayload;
            options.DefaultCorrelationIdInputMode = InboxCorrelationIdInputMode.IgnoreIncoming;
        });
        using var scope = provider.CreateScope();

        var opened = await OpenAsync(
            scope,
            idempotencyKey: "ignored-key",
            correlationId: "ignored-correlation");

        Assert.Equal(opened.Entry.PayloadHash, opened.EffectiveIdempotencyKey);
        Assert.NotEqual("ignored-correlation", opened.EffectiveCorrelationId);
    }

    [Fact]
    public async Task Request_modes_override_named_policy_and_global_modes()
    {
        using var provider = CreateProvider(options =>
        {
            options.DefaultIdempotencyKeyResolutionMode = InboxIdempotencyKeyResolutionMode.RecomputeFromPayload;
            options.DefaultCorrelationIdInputMode = InboxCorrelationIdInputMode.IgnoreIncoming;
            options.AddInboxPolicy("canonical", policy =>
            {
                policy.IdempotencyKeyResolutionMode = InboxIdempotencyKeyResolutionMode.RecomputeFromPayload;
                policy.CorrelationIdInputMode = InboxCorrelationIdInputMode.IgnoreIncoming;
            });
        });
        using var scope = provider.CreateScope();

        var opened = await OpenAsync(
            scope,
            idempotencyKey: "trusted-key",
            correlationId: "trusted-correlation",
            policyName: "canonical",
            idempotencyMode: InboxIdempotencyKeyResolutionMode.PreferIncoming,
            correlationInputMode: InboxCorrelationIdInputMode.PreferIncoming);

        Assert.Equal("trusted-key", opened.EffectiveIdempotencyKey);
        Assert.Equal("trusted-correlation", opened.EffectiveCorrelationId);
    }

    [Fact]
    public async Task Default_global_enum_values_fall_back_to_compatible_modes()
    {
        using var provider = CreateProvider(options =>
        {
            options.DefaultIdempotencyKeyResolutionMode = InboxIdempotencyKeyResolutionMode.Default;
            options.DefaultCorrelationIdInputMode = InboxCorrelationIdInputMode.Default;
        });
        using var scope = provider.CreateScope();

        var opened = await OpenAsync(
            scope,
            idempotencyKey: "incoming-key",
            correlationId: "incoming-correlation");

        Assert.Equal("incoming-key", opened.EffectiveIdempotencyKey);
        Assert.Equal("incoming-correlation", opened.EffectiveCorrelationId);
    }

    [Fact]
    public async Task Recompute_mode_requires_a_payload_even_when_an_incoming_key_exists()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();

        var result = await scope.ServiceProvider.GetRequiredService<IInboxService>()
            .OpenOrContinueAsync(new InboxOpenRequest
            {
                Source = "http",
                Operation = "POST /orders",
                IdempotencyKey = "ignored-key",
                IdempotencyKeyResolutionMode = InboxIdempotencyKeyResolutionMode.RecomputeFromPayload
            });

        Assert.Equal(InboxOpenState.MissingIdempotencyKey, result.State);
    }

    [Fact]
    public async Task Invalid_concrete_modes_are_rejected()
    {
        using var provider = CreateProvider();

        using (var keyScope = provider.CreateScope())
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await OpenAsync(
                    keyScope,
                    idempotencyKey: "key",
                    correlationId: "correlation",
                    idempotencyMode: (InboxIdempotencyKeyResolutionMode)999));
        }

        using (var correlationScope = provider.CreateScope())
        {
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await OpenAsync(
                    correlationScope,
                    idempotencyKey: "key",
                    correlationId: "correlation",
                    correlationInputMode: (InboxCorrelationIdInputMode)999));
        }
    }

    private static ValueTask<InboxOpenResult> OpenAsync(
        IServiceScope scope,
        string idempotencyKey,
        string correlationId,
        string policyName = null,
        bool? allowPayloadHash = null,
        InboxIdempotencyKeyResolutionMode idempotencyMode = InboxIdempotencyKeyResolutionMode.Default,
        InboxCorrelationIdInputMode correlationInputMode = InboxCorrelationIdInputMode.Default)
        => scope.ServiceProvider.GetRequiredService<IInboxService>()
            .OpenOrContinueAsync(new InboxOpenRequest
            {
                Source = "http",
                Operation = "POST /orders",
                IdempotencyKey = idempotencyKey,
                CorrelationId = correlationId,
                Payload = new TestPayload("order-1"),
                PolicyName = policyName,
                AllowPayloadHashAsIdempotencyKey = allowPayloadHash,
                IdempotencyKeyResolutionMode = idempotencyMode,
                CorrelationIdInputMode = correlationInputMode
            });

    private static ServiceProvider CreateProvider(Action<SquirrelBoxOptions> configure = null)
    {
        var services = new ServiceCollection();
        services.AddSquirrelBox(configure).UseInMemory();
        return services.BuildServiceProvider();
    }

    private sealed record TestPayload(string Id);
}
