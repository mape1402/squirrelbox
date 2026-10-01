using System.Reflection;

namespace SquirrelBox;

/// <summary>
/// Configures the default behavior used by the SquirrelBox inbox core.
/// </summary>
public sealed class SquirrelBoxOptions
{
    /// <summary>
    /// Gets or sets the default execution mode assigned to newly opened inbox entries.
    /// </summary>
    public InboxExecutionMode DefaultExecutionMode { get; set; } = InboxExecutionMode.Inline;

    /// <summary>
    /// Gets or sets the default lifetime assigned to newly opened inbox entries.
    /// </summary>
    public TimeSpan? DefaultEntryLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets or sets whether a semantic payload hash can be used as the idempotency key when no explicit key is received.
    /// </summary>
    public bool AllowPayloadHashAsIdempotencyKey { get; set; } = true;

    /// <summary>
    /// Gets or sets the owner name used when an open request does not provide one.
    /// </summary>
    public string DefaultOwner { get; set; } = "manual";

    /// <summary>
    /// Gets the default idempotency policy used when an entrypoint does not select a named policy.
    /// </summary>
    public InboxIdempotencyPolicy DefaultInboxPolicy { get; } = new()
    {
        Name = "default"
    };

    /// <summary>
    /// Gets the named idempotency policies available to entrypoints.
    /// </summary>
    public IDictionary<string, InboxIdempotencyPolicy> InboxPolicies { get; } =
        new Dictionary<string, InboxIdempotencyPolicy>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the assemblies scanned for <see cref="InboxFingerprintProfile"/> implementations.
    /// </summary>
    public IList<Assembly> FingerprintProfileAssemblies { get; } = [];

    /// <summary>
    /// Gets the assemblies scanned for declared SquirrelBox operations.
    /// </summary>
    public IList<Assembly> OperationAssemblies { get; } = [];

    /// <summary>
    /// Gets the assemblies scanned for <see cref="IOutboxProfile"/> implementations.
    /// </summary>
    public IList<Assembly> OutboxProfileAssemblies { get; } = [];

    /// <summary>
    /// Gets the outbox options.
    /// </summary>
    public OutboxOptions Outbox { get; } = new();

    /// <summary>
    /// Gets the policy options used to classify duplicate, conflict, and missing key outcomes.
    /// </summary>
    public InboxPolicyOptions Policies { get; } = new();

    /// <summary>
    /// Adds an assembly to the fingerprint profile and operation discovery lists.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The same options instance for fluent configuration.</returns>
    public SquirrelBoxOptions ScanAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (!FingerprintProfileAssemblies.Contains(assembly))
            FingerprintProfileAssemblies.Add(assembly);

        if (!OperationAssemblies.Contains(assembly))
            OperationAssemblies.Add(assembly);

        if (!OutboxProfileAssemblies.Contains(assembly))
            OutboxProfileAssemblies.Add(assembly);

        return this;
    }

    /// <summary>
    /// Adds or updates a named inbox idempotency policy.
    /// </summary>
    /// <param name="name">The policy name.</param>
    /// <param name="configure">The policy configuration callback.</param>
    /// <returns>The same options instance for fluent configuration.</returns>
    public SquirrelBoxOptions AddInboxPolicy(
        string name,
        Action<InboxIdempotencyPolicy> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);

        if (!InboxPolicies.TryGetValue(name, out var policy))
        {
            policy = new InboxIdempotencyPolicy
            {
                Name = name
            };
            InboxPolicies[name] = policy;
        }

        configure(policy);
        policy.Name = name;
        return this;
    }

    /// <summary>
    /// Adds the assembly containing <typeparamref name="TMarker"/> to the discovery lists.
    /// </summary>
    /// <typeparam name="TMarker">A marker type from the assembly to scan.</typeparam>
    /// <returns>The same options instance for fluent configuration.</returns>
    public SquirrelBoxOptions ScanAssemblyContaining<TMarker>()
        => ScanAssembly(typeof(TMarker).Assembly);
}
