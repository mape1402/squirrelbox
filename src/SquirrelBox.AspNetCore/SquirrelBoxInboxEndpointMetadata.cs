namespace SquirrelBox.AspNetCore;

internal sealed class SquirrelBoxInboxEndpointMetadata : ISquirrelBoxInboxMetadata
{
    public SquirrelBoxInboxEndpointMetadata(SquirrelBoxInboxEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ArgumentName = string.IsNullOrWhiteSpace(options.ArgumentName) ? "request" : options.ArgumentName;
        PolicyName = options.PolicyName;
        EntryLifetime = options.EntryLifetime;
        CompletedLock = options.CompletedLock;
        ExecutionMode = options.ExecutionMode;
        Deferred = CloneDeferred(options.Deferred);
    }

    public string ArgumentName { get; }

    public string PolicyName { get; }

    public TimeSpan? EntryLifetime { get; }

    public InboxCompletedLockMode CompletedLock { get; }

    public InboxExecutionMode? ExecutionMode { get; }

    public InboxDeferredPolicyOptions Deferred { get; }

    private static InboxDeferredPolicyOptions CloneDeferred(InboxDeferredPolicyOptions deferred)
        => new()
        {
            Lane = deferred.Lane,
            MaxAttempts = deferred.MaxAttempts,
            Delay = deferred.Delay,
            MaxDelay = deferred.MaxDelay,
            Backoff = deferred.Backoff,
            JitterRatio = deferred.JitterRatio,
            InProgressTimeout = deferred.InProgressTimeout
        };
}
