namespace SquirrelBox.Messaging;

/// <summary>
/// Configures a messaging inbox policy.
/// </summary>
public sealed class InboxMessagePolicyBuilder
{
    private readonly InboxMessagePolicy _policy;

    internal InboxMessagePolicyBuilder(InboxMessagePolicy policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <summary>
    /// Assigns a friendly policy name.
    /// </summary>
    /// <param name="name">The policy name.</param>
    /// <returns>The same builder for chaining.</returns>
    public InboxMessagePolicyBuilder Named(string name)
    {
        _policy.Name = name;
        return this;
    }

    /// <summary>
    /// Matches a message contract version.
    /// </summary>
    /// <param name="version">The version to match.</param>
    /// <returns>The same builder for chaining.</returns>
    public InboxMessagePolicyBuilder Version(string version)
    {
        _policy.Version = version;
        return this;
    }

    /// <summary>
    /// Matches a subscription or consumer group.
    /// </summary>
    /// <param name="subscription">The subscription to match.</param>
    /// <returns>The same builder for chaining.</returns>
    public InboxMessagePolicyBuilder Subscription(string subscription)
    {
        _policy.Subscription = subscription;
        return this;
    }

    /// <summary>
    /// Matches a message operation.
    /// </summary>
    /// <param name="operation">The operation to match.</param>
    /// <returns>The same builder for chaining.</returns>
    public InboxMessagePolicyBuilder Operation(string operation)
    {
        _policy.Operation = operation;
        return this;
    }

    /// <summary>
    /// Applies a named core inbox policy.
    /// </summary>
    /// <param name="policyName">The core policy name.</param>
    /// <returns>The same builder for chaining.</returns>
    public InboxMessagePolicyBuilder UseCorePolicy(string policyName)
    {
        _policy.CorePolicyName = policyName;
        return this;
    }

    /// <summary>
    /// Sets the idempotency window for entries opened by this policy.
    /// </summary>
    /// <param name="lifetime">The entry lifetime.</param>
    /// <returns>The same builder for chaining.</returns>
    public InboxMessagePolicyBuilder WithEntryLifetime(TimeSpan lifetime)
    {
        _policy.EntryLifetime = lifetime;
        return this;
    }

    /// <summary>
    /// Sets the completed duplicate lock behavior for entries opened by this policy.
    /// </summary>
    /// <param name="completedLock">The completed lock behavior.</param>
    /// <returns>The same builder for chaining.</returns>
    public InboxMessagePolicyBuilder WithCompletedLock(InboxCompletedLockMode completedLock)
    {
        _policy.CompletedLock = completedLock;
        return this;
    }

    /// <summary>
    /// Sets the execution mode for entries opened by this policy.
    /// </summary>
    /// <param name="executionMode">The execution mode.</param>
    /// <returns>The same builder for chaining.</returns>
    public InboxMessagePolicyBuilder WithExecutionMode(InboxExecutionMode executionMode)
    {
        _policy.ExecutionMode = executionMode;
        return this;
    }

    /// <summary>
    /// Sets whether payload hashes can be used as idempotency keys for this policy.
    /// </summary>
    /// <param name="enabled">Whether payload-hash keys are enabled.</param>
    /// <returns>The same builder for chaining.</returns>
    public InboxMessagePolicyBuilder AllowPayloadHashKeys(bool enabled = true)
    {
        _policy.AllowPayloadHashAsIdempotencyKey = enabled;
        return this;
    }
}
