namespace SquirrelBox.Messaging;

/// <summary>
/// Builds messaging inbox policies for a profile.
/// </summary>
public sealed class InboxMessagePolicyProfileBuilder
{
    private readonly IList<InboxMessagePolicy> _policies;

    internal InboxMessagePolicyProfileBuilder(IList<InboxMessagePolicy> policies)
    {
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
    }

    /// <summary>
    /// Adds a catch-all inbox policy.
    /// </summary>
    /// <returns>A builder for the new policy.</returns>
    public InboxMessagePolicyBuilder ForAll()
        => Add(new InboxMessagePolicy());

    /// <summary>
    /// Adds an inbox policy for a topic.
    /// </summary>
    /// <param name="topic">The topic or exchange to match.</param>
    /// <returns>A builder for the new policy.</returns>
    public InboxMessagePolicyBuilder ForTopic(string topic)
        => Add(new InboxMessagePolicy { Topic = topic });

    /// <summary>
    /// Adds an inbox policy with explicit match values.
    /// </summary>
    /// <param name="topic">The topic or exchange to match.</param>
    /// <param name="version">The message contract version to match.</param>
    /// <param name="subscription">The subscription or consumer group to match.</param>
    /// <param name="operation">The operation to match.</param>
    /// <returns>A builder for the new policy.</returns>
    public InboxMessagePolicyBuilder Match(
        string topic = null,
        string version = null,
        string subscription = null,
        string operation = null)
        => Add(new InboxMessagePolicy
        {
            Topic = topic,
            Version = version,
            Subscription = subscription,
            Operation = operation
        });

    private InboxMessagePolicyBuilder Add(InboxMessagePolicy policy)
    {
        policy.Order = _policies.Count;
        _policies.Add(policy);
        return new InboxMessagePolicyBuilder(policy);
    }
}
