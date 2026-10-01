using System.Reflection;

namespace SquirrelBox.Messaging;

/// <summary>
/// Configures transport-neutral messaging inbox behavior.
/// </summary>
public sealed class SquirrelBoxMessagingOptions
{
    /// <summary>
    /// Gets the assemblies scanned for <see cref="InboxMessagePolicyProfile"/> implementations.
    /// </summary>
    public IList<Assembly> ProfileAssemblies { get; } = [];

    /// <summary>
    /// Gets or sets the metadata name used when the broker message id becomes the idempotency key.
    /// </summary>
    public string MessageIdMetadataName { get; set; } = "message-id";

    /// <summary>
    /// Gets or sets whether the broker message id can be used as an idempotency key.
    /// </summary>
    public bool UseMessageIdWhenKeyIsMissing { get; set; } = true;

    /// <summary>
    /// Gets or sets whether the correlation id can be used as an idempotency key when no key or message id exists.
    /// </summary>
    public bool UseCorrelationIdWhenKeyIsMissing { get; set; }

    /// <summary>
    /// Gets or sets whether a semantic payload hash can be used when no metadata key exists.
    /// </summary>
    public bool AllowPayloadHashAsIdempotencyKey { get; set; } = true;

    /// <summary>
    /// Gets or sets the operation resolver. The default includes topic, version, subscription, and operation.
    /// </summary>
    public Func<InboxMessageContext, string> OperationResolver { get; set; } = context =>
    {
        var topic = string.IsNullOrWhiteSpace(context.Topic) ? "unknown-topic" : context.Topic;
        var version = string.IsNullOrWhiteSpace(context.Version) ? "unversioned" : context.Version;
        var subscription = string.IsNullOrWhiteSpace(context.Subscription) ? "default" : context.Subscription;
        var operation = string.IsNullOrWhiteSpace(context.Operation) ? "message" : context.Operation;
        return $"{topic}:{version}/{subscription}/{operation}";
    };

    /// <summary>
    /// Gets or sets an optional execution mode resolver for incoming messages.
    /// </summary>
    public Func<InboxMessageContext, InboxExecutionMode?> ExecutionModeResolver { get; set; }

    /// <summary>
    /// Adds an assembly to the messaging inbox policy profile discovery list.
    /// </summary>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The same options instance for fluent configuration.</returns>
    public SquirrelBoxMessagingOptions ScanAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (!ProfileAssemblies.Contains(assembly))
            ProfileAssemblies.Add(assembly);

        return this;
    }

    /// <summary>
    /// Adds the assembly containing <typeparamref name="TMarker"/> to the messaging inbox policy profile discovery list.
    /// </summary>
    /// <typeparam name="TMarker">A marker type from the assembly to scan.</typeparam>
    /// <returns>The same options instance for fluent configuration.</returns>
    public SquirrelBoxMessagingOptions ScanAssemblyContaining<TMarker>()
        => ScanAssembly(typeof(TMarker).Assembly);
}
