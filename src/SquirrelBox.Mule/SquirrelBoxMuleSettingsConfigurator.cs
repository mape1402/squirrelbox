using Microsoft.Extensions.Options;
using Mule;

namespace SquirrelBox.Mule;

internal sealed class SquirrelBoxMuleSettingsConfigurator : IConfigureOptions<MuleSettings>
{
    private readonly SquirrelBoxOptions _options;

    public SquirrelBoxMuleSettingsConfigurator(IOptions<SquirrelBoxOptions> options)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public void Configure(MuleSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        ConfigurePolicy(settings, _options.DefaultInboxPolicy);
        foreach (var policy in _options.InboxPolicies.Values)
            ConfigurePolicy(settings, policy);
    }

    private static void ConfigurePolicy(MuleSettings settings, InboxIdempotencyPolicy policy)
    {
        if (policy?.Deferred is not { } deferred || !HasRetryConfiguration(deferred))
            return;

        var laneName = ResolveLane(policy, deferred);
        if (string.IsNullOrWhiteSpace(laneName))
            return;

        if (!settings.Lanes.TryGetValue(laneName, out var lane))
        {
            lane = new MuleLaneSettings();
            settings.Lanes[laneName] = lane;
        }

        var current = lane.RetryPolicy;
        lane.RetryPolicy = new MuleRetryPolicy
        {
            MaxAttempts = deferred.MaxAttempts ??
                          current?.MaxAttempts ??
                          settings.RetryPolicy?.MaxAttempts ??
                          FirstPositive(lane.MaxAttempts, settings.MaxAttempts),
            Delay = deferred.Delay ??
                    current?.Delay ??
                    settings.RetryPolicy?.Delay ??
                    FirstPositive(lane.RetryDelay, settings.RetryDelay),
            MaxDelay = deferred.MaxDelay ?? current?.MaxDelay ?? settings.RetryPolicy?.MaxDelay,
            Backoff = MapBackoff(deferred.Backoff ?? MapBackoff(current?.Backoff ?? settings.RetryPolicy?.Backoff ?? MuleRetryBackoff.Fixed)),
            JitterRatio = deferred.JitterRatio ??
                          current?.JitterRatio ??
                          settings.RetryPolicy?.JitterRatio ??
                          0
        };
    }

    private static string ResolveLane(InboxIdempotencyPolicy policy, InboxDeferredPolicyOptions deferred)
        => !string.IsNullOrWhiteSpace(deferred.Lane)
            ? deferred.Lane
            : !string.IsNullOrWhiteSpace(policy.Name)
                ? $"squirrelbox.{policy.Name}"
                : null;

    private static bool HasRetryConfiguration(InboxDeferredPolicyOptions deferred)
        => deferred.MaxAttempts is not null ||
           deferred.Delay is not null ||
           deferred.MaxDelay is not null ||
           deferred.Backoff is not null ||
           deferred.JitterRatio is not null;

    private static T FirstPositive<T>(T first, T second)
        where T : struct, IComparable<T>
    {
        var zero = default(T);
        if (first.CompareTo(zero) > 0)
            return first;

        return second.CompareTo(zero) > 0 ? second : zero;
    }

    private static MuleRetryBackoff MapBackoff(InboxRetryBackoff backoff)
        => backoff switch
        {
            InboxRetryBackoff.Linear => MuleRetryBackoff.Linear,
            InboxRetryBackoff.Exponential => MuleRetryBackoff.Exponential,
            _ => MuleRetryBackoff.Fixed
        };

    private static InboxRetryBackoff MapBackoff(MuleRetryBackoff backoff)
        => backoff switch
        {
            MuleRetryBackoff.Linear => InboxRetryBackoff.Linear,
            MuleRetryBackoff.Exponential => InboxRetryBackoff.Exponential,
            _ => InboxRetryBackoff.Fixed
        };
}
