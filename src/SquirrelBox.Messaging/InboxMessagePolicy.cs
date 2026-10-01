namespace SquirrelBox.Messaging;

/// <summary>
/// Defines when and how inbox idempotency applies to an incoming message.
/// </summary>
public sealed class InboxMessagePolicy
{
    /// <summary>
    /// Gets or sets the optional policy name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the topic or exchange matched by the policy.
    /// </summary>
    public string Topic { get; set; }

    /// <summary>
    /// Gets or sets the message contract version matched by the policy.
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// Gets or sets the subscription or consumer group matched by the policy.
    /// </summary>
    public string Subscription { get; set; }

    /// <summary>
    /// Gets or sets the operation matched by the policy.
    /// </summary>
    public string Operation { get; set; }

    /// <summary>
    /// Gets or sets the named core inbox policy to apply.
    /// </summary>
    public string CorePolicyName { get; set; }

    /// <summary>
    /// Gets or sets the entry lifetime override to apply.
    /// </summary>
    public TimeSpan? EntryLifetime { get; set; }

    /// <summary>
    /// Gets or sets how completed entries opened by this policy block duplicates.
    /// </summary>
    public InboxCompletedLockMode CompletedLock { get; set; } = InboxCompletedLockMode.Default;

    /// <summary>
    /// Gets or sets an optional execution mode override.
    /// </summary>
    public InboxExecutionMode? ExecutionMode { get; set; }

    /// <summary>
    /// Gets or sets whether payload hashes can be used as idempotency keys for this policy.
    /// </summary>
    public bool? AllowPayloadHashAsIdempotencyKey { get; set; }

    /// <summary>
    /// Gets deferred execution settings selected by this messaging policy.
    /// </summary>
    public InboxDeferredPolicyOptions Deferred { get; } = new();

    internal int Order { get; set; }

    internal bool Matches(InboxMessageContext context)
        => Matches(Topic, context.Topic) &&
           Matches(Version, context.Version) &&
           Matches(Subscription, context.Subscription) &&
           Matches(Operation, context.Operation);

    internal int Specificity
        => Count(Topic) + Count(Version) + Count(Subscription) + Count(Operation);

    private static bool Matches(string expected, string actual)
        => string.IsNullOrWhiteSpace(expected) ||
           string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

    private static int Count(string value)
        => string.IsNullOrWhiteSpace(value) ? 0 : 1;
}
