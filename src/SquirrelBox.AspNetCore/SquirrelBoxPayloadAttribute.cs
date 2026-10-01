using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SquirrelBox.AspNetCore;

/// <summary>
/// Opens or continues a SquirrelBox inbox context from a bound MVC action payload.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class SquirrelBoxPayloadAttribute : Attribute, IAsyncActionFilter, ISquirrelBoxPayloadMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxPayloadAttribute"/> class.
    /// </summary>
    /// <param name="argumentName">The action argument name that contains the payload.</param>
    public SquirrelBoxPayloadAttribute(string argumentName = "request")
    {
        ArgumentName = string.IsNullOrWhiteSpace(argumentName) ? "request" : argumentName;
    }

    /// <inheritdoc />
    public string ArgumentName { get; }

    /// <summary>
    /// Gets or sets the named inbox policy selected by this action.
    /// </summary>
    public string PolicyName { get; set; }

    /// <summary>
    /// Gets or sets the named inbox policy selected by this action.
    /// </summary>
    public string Policy
    {
        get => PolicyName;
        set => PolicyName = value;
    }

    /// <summary>
    /// Gets or sets the entry lifetime override in seconds.
    /// </summary>
    public int TtlSeconds { get; set; }

    /// <summary>
    /// Gets or sets the entry lifetime override in seconds.
    /// </summary>
    public int WindowSeconds
    {
        get => TtlSeconds;
        set => TtlSeconds = value;
    }

    /// <inheritdoc />
    public TimeSpan? EntryLifetime => TtlSeconds > 0 ? TimeSpan.FromSeconds(TtlSeconds) : null;

    /// <inheritdoc />
    public InboxCompletedLockMode CompletedLock { get; set; } = InboxCompletedLockMode.Default;

    /// <summary>
    /// Gets or sets whether this action should defer execution through the configured deferred adapter.
    /// </summary>
    public bool DeferExecution { get; set; }

    /// <summary>
    /// Gets or sets the Mule lane or deferred adapter lane selected by this action.
    /// </summary>
    public string DeferredLane { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of deferred execution attempts.
    /// </summary>
    public int RetryMaxAttempts { get; set; }

    /// <summary>
    /// Gets or sets the initial deferred retry delay in seconds.
    /// </summary>
    public int RetryDelaySeconds { get; set; }

    /// <summary>
    /// Gets or sets the maximum deferred retry delay in seconds.
    /// </summary>
    public int RetryMaxDelaySeconds { get; set; }

    /// <summary>
    /// Gets or sets the deferred retry backoff strategy.
    /// </summary>
    public InboxRetryBackoff RetryBackoff { get; set; } = InboxRetryBackoff.Fixed;

    /// <summary>
    /// Gets or sets the deferred retry jitter ratio.
    /// </summary>
    public double RetryJitterRatio { get; set; }

    /// <summary>
    /// Gets or sets how long an active deferred inbox entry can remain in progress before a new attempt may reopen it.
    /// </summary>
    public int InProgressTimeoutSeconds { get; set; }

    /// <inheritdoc />
    public InboxExecutionMode? ExecutionMode => DeferExecution ? InboxExecutionMode.Deferred : null;

    /// <inheritdoc />
    public InboxDeferredPolicyOptions Deferred => new()
    {
        Lane = DeferredLane,
        MaxAttempts = RetryMaxAttempts > 0 ? RetryMaxAttempts : null,
        Delay = RetryDelaySeconds > 0 ? TimeSpan.FromSeconds(RetryDelaySeconds) : null,
        MaxDelay = RetryMaxDelaySeconds > 0 ? TimeSpan.FromSeconds(RetryMaxDelaySeconds) : null,
        Backoff = RetryBackoff == InboxRetryBackoff.Fixed ? null : RetryBackoff,
        JitterRatio = RetryJitterRatio > 0 ? RetryJitterRatio : null,
        InProgressTimeout = InProgressTimeoutSeconds > 0 ? TimeSpan.FromSeconds(InProgressTimeoutSeconds) : null
    };

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        if (!context.ActionArguments.TryGetValue(ArgumentName, out var payload) ||
            payload is null)
        {
            await next();
            return;
        }

        var services = context.HttpContext.RequestServices;
        var accepted = await SquirrelBoxPayloadFilterExecutor.TryAcceptAsync(
            context.HttpContext,
            payload,
            this,
            services.GetRequiredService<IInboxService>(),
            services.GetRequiredService<IInboxPolicyResolver>(),
            services.GetRequiredService<IOptions<SquirrelBoxAspNetCoreOptions>>().Value);

        if (!accepted)
        {
            context.Result = new EmptyResult();
            return;
        }

        await next();
    }
}
