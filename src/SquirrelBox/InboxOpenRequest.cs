namespace SquirrelBox;

/// <summary>
/// Describes a request to open or continue an inbox context.
/// </summary>
public sealed class InboxOpenRequest
{
    /// <summary>
    /// Gets the transport or component source.
    /// </summary>
    public string Source { get; init; }

    /// <summary>
    /// Gets the protected operation name.
    /// </summary>
    public string Operation { get; init; }

    /// <summary>
    /// Gets the explicit idempotency key, when supplied.
    /// </summary>
    public string IdempotencyKey { get; init; }

    /// <summary>
    /// Gets the metadata/header name used to receive the idempotency key.
    /// </summary>
    public string IdempotencyKeyName { get; init; }

    /// <summary>
    /// Gets the payload used for semantic hashing.
    /// </summary>
    public object Payload { get; init; }

    /// <summary>
    /// Gets the payload type name.
    /// </summary>
    public string PayloadType { get; init; }

    /// <summary>
    /// Gets the correlation id.
    /// </summary>
    public string CorrelationId { get; init; }

    /// <summary>
    /// Gets the metadata/header name used to receive the correlation id.
    /// </summary>
    public string CorrelationIdName { get; init; }

    /// <summary>
    /// Gets the trace id for the current attempt.
    /// </summary>
    public string TraceId { get; init; }

    /// <summary>
    /// Gets the metadata/header name used to receive the trace id.
    /// </summary>
    public string TraceIdName { get; init; }

    /// <summary>
    /// Gets the metadata/header name used to propagate the generated attempt id.
    /// </summary>
    public string AttemptIdName { get; init; }

    /// <summary>
    /// Gets the component that owns the opened context.
    /// </summary>
    public string Owner { get; init; }

    /// <summary>
    /// Gets whether the opener should complete or fail the context.
    /// </summary>
    public bool OwnsCompletion { get; init; } = true;

    /// <summary>
    /// Gets an optional override for payload-hash key fallback.
    /// </summary>
    public bool? AllowPayloadHashAsIdempotencyKey { get; init; }

    /// <summary>
    /// Gets an optional execution mode override.
    /// </summary>
    public InboxExecutionMode? ExecutionMode { get; init; }

    /// <summary>
    /// Gets the named idempotency policy selected by the entrypoint.
    /// </summary>
    public string PolicyName { get; init; }

    /// <summary>
    /// Gets an optional entry lifetime override selected by the entrypoint.
    /// </summary>
    public TimeSpan? EntryLifetime { get; init; }

    /// <summary>
    /// Gets an optional completed duplicate lock override selected by the entrypoint.
    /// </summary>
    public InboxCompletedLockMode CompletedLock { get; init; } = InboxCompletedLockMode.Default;

    /// <summary>
    /// Gets an optional expiration timestamp.
    /// </summary>
    public DateTimeOffset? ExpiresOnUtc { get; init; }

    /// <summary>
    /// Gets metadata copied to the created inbox entry.
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a typed inbox open request.
    /// </summary>
    public static InboxOpenRequest For<TPayload>(
        string source,
        string operation,
        string idempotencyKey = null,
        TPayload payload = default,
        string correlationId = null,
        string owner = null,
        bool ownsCompletion = true,
        bool? allowPayloadHashAsIdempotencyKey = null,
        InboxExecutionMode? executionMode = null,
        DateTimeOffset? expiresOnUtc = null,
        string policyName = null,
        TimeSpan? entryLifetime = null,
        InboxCompletedLockMode completedLock = InboxCompletedLockMode.Default)
        => new()
        {
            Source = source,
            Operation = operation,
            IdempotencyKey = idempotencyKey,
            Payload = payload,
            PayloadType = typeof(TPayload).AssemblyQualifiedName,
            CorrelationId = correlationId,
            Owner = owner,
            OwnsCompletion = ownsCompletion,
            AllowPayloadHashAsIdempotencyKey = allowPayloadHashAsIdempotencyKey,
            ExecutionMode = executionMode,
            ExpiresOnUtc = expiresOnUtc,
            PolicyName = policyName,
            EntryLifetime = entryLifetime,
            CompletedLock = completedLock
        };
}
