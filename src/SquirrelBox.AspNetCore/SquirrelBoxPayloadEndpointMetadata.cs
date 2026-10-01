namespace SquirrelBox.AspNetCore;

internal sealed class SquirrelBoxPayloadEndpointMetadata : ISquirrelBoxPayloadMetadata
{
    public SquirrelBoxPayloadEndpointMetadata(SquirrelBoxPayloadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        ArgumentName = string.IsNullOrWhiteSpace(options.ArgumentName) ? "request" : options.ArgumentName;
        PolicyName = options.PolicyName;
        EntryLifetime = options.EntryLifetime;
        CompletedLock = options.CompletedLock;
    }

    public string ArgumentName { get; }

    public string PolicyName { get; }

    public TimeSpan? EntryLifetime { get; }

    public InboxCompletedLockMode CompletedLock { get; }
}
