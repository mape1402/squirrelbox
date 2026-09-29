namespace SquirrelBox;

/// <summary>
/// Provides input data used by SquirrelBox identity factories.
/// </summary>
public sealed class InboxIdentityFactoryContext
{
    /// <summary>
    /// Gets or sets the inbox source.
    /// </summary>
    public string Source { get; init; }

    /// <summary>
    /// Gets or sets the inbox operation.
    /// </summary>
    public string Operation { get; init; }

    /// <summary>
    /// Gets or sets the effective idempotency key.
    /// </summary>
    public string IdempotencyKey { get; init; }

    /// <summary>
    /// Gets or sets how the idempotency key was resolved.
    /// </summary>
    public InboxIdempotencyKeySource IdempotencyKeySource { get; init; }

    /// <summary>
    /// Gets or sets the incoming correlation id, when supplied.
    /// </summary>
    public string IncomingCorrelationId { get; init; }

    /// <summary>
    /// Gets or sets the incoming trace id, when supplied.
    /// </summary>
    public string IncomingTraceId { get; init; }

    /// <summary>
    /// Gets or sets the request payload.
    /// </summary>
    public object Payload { get; init; }

    /// <summary>
    /// Gets or sets the request metadata.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
