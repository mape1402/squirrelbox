using Mule;
using Microsoft.Extensions.Options;

namespace SquirrelBox.Mule;

/// <summary>
/// Default scheduler that enqueues Mule durable actions for the current inbox context.
/// </summary>
public sealed class InboxMuleScheduler : IInboxMuleScheduler
{
    private readonly IInboxService _inbox;
    private readonly IMuleClient _mule;
    private readonly IOptions<MuleSettings> _settings;

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxMuleScheduler"/> class.
    /// </summary>
    /// <param name="inbox">The current inbox service.</param>
    /// <param name="mule">The Mule client.</param>
    /// <param name="settings">The Mule settings used to resolve effective retry metadata.</param>
    public InboxMuleScheduler(
        IInboxService inbox,
        IMuleClient mule,
        IOptions<MuleSettings> settings)
    {
        _inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
        _mule = mule ?? throw new ArgumentNullException(nameof(mule));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <inheritdoc />
    public ValueTask<Guid> EnqueueCurrentAsync<TPayload>(
        ActionKey key,
        TPayload payload,
        Action<EnqueueOptions> configure = null,
        CancellationToken cancellationToken = default)
    {
        var context = _inbox.Current ?? throw new InboxContextUnavailableException();

        return _mule.EnqueueAsync(key, payload, options =>
        {
            options.CorrelationId ??= context.Entry.CorrelationId;
            options.DeduplicationKey ??= context.Entry.Id.ToString();
            ApplyDeferredOptions(options, context.Entry);
            configure?.Invoke(options);
            WriteSquirrelBoxMetadata(options, context);
        }, cancellationToken);
    }

    private void ApplyDeferredOptions(EnqueueOptions options, InboxEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Deferred?.Lane) &&
            (string.IsNullOrWhiteSpace(options.Lane) ||
             string.Equals(options.Lane, MuleSettings.DefaultLane, StringComparison.OrdinalIgnoreCase)))
        {
            options.Lane = entry.Deferred.Lane;
        }
    }

    private void WriteSquirrelBoxMetadata(EnqueueOptions options, InboxContext context)
    {
        var entry = context.Entry;
        var deferred = entry.Deferred;

        options.Metadata[SquirrelBoxMuleMetadata.InboxEntryId] = entry.Id.ToString();
        options.Metadata[SquirrelBoxMuleMetadata.IdempotencyKey] = context.EffectiveIdempotencyKey;

        if (!string.IsNullOrWhiteSpace(deferred?.PolicyName))
            options.Metadata[SquirrelBoxMuleMetadata.DeferredPolicyName] = deferred.PolicyName;

        if (!string.IsNullOrWhiteSpace(options.Lane))
            options.Metadata[SquirrelBoxMuleMetadata.DeferredLane] = options.Lane;

        var maxAttempts = ResolveMaxAttempts(options.Lane, deferred);
        if (maxAttempts > 0)
            options.Metadata[SquirrelBoxMuleMetadata.DeferredMaxAttempts] = maxAttempts.ToString();

        if (deferred?.Delay is { } delay)
            options.Metadata[SquirrelBoxMuleMetadata.DeferredRetryDelayMilliseconds] = Convert.ToInt64(delay.TotalMilliseconds).ToString();

        if (deferred?.MaxDelay is { } maxDelay)
            options.Metadata[SquirrelBoxMuleMetadata.DeferredRetryMaxDelayMilliseconds] = Convert.ToInt64(maxDelay.TotalMilliseconds).ToString();

        if (deferred?.Backoff is { } backoff)
            options.Metadata[SquirrelBoxMuleMetadata.DeferredRetryBackoff] = backoff.ToString();

        if (deferred?.JitterRatio is { } jitterRatio)
            options.Metadata[SquirrelBoxMuleMetadata.DeferredRetryJitterRatio] = jitterRatio.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private int ResolveMaxAttempts(string laneName, InboxDeferredExecutionOptions deferred)
    {
        if (deferred?.MaxAttempts is { } maxAttempts && maxAttempts > 0)
            return maxAttempts;

        var settings = _settings.Value;
        if (!string.IsNullOrWhiteSpace(laneName) &&
            settings.Lanes.TryGetValue(laneName, out var lane))
        {
            if (lane.RetryPolicy?.MaxAttempts > 0)
                return lane.RetryPolicy.MaxAttempts;

            if (lane.MaxAttempts > 0)
                return lane.MaxAttempts;
        }

        if (settings.RetryPolicy?.MaxAttempts > 0)
            return settings.RetryPolicy.MaxAttempts;

        return settings.MaxAttempts;
    }
}
