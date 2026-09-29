namespace SquirrelBox;

/// <summary>
/// Defines default metadata names used by SquirrelBox identity propagation.
/// </summary>
public static class SquirrelBoxMetadataNames
{
    /// <summary>
    /// Gets the default metadata name for idempotency keys.
    /// </summary>
    public const string IdempotencyKey = "idempotency-key";

    /// <summary>
    /// Gets the default metadata name for correlation ids.
    /// </summary>
    public const string CorrelationId = "correlation-id";

    /// <summary>
    /// Gets the default metadata name for trace ids.
    /// </summary>
    public const string TraceId = "trace-id";

    /// <summary>
    /// Gets the default metadata name for attempt ids.
    /// </summary>
    public const string AttemptId = "attempt-id";
}
