namespace SquirrelBox;

/// <summary>
/// Represents the transport-neutral SquirrelBox identity metadata propagated across adapters.
/// </summary>
public sealed class SquirrelBoxMetadata
{
    /// <summary>
    /// Gets an empty metadata instance.
    /// </summary>
    public static SquirrelBoxMetadata Empty { get; } = new();

    /// <summary>
    /// Gets or sets the effective idempotency key.
    /// </summary>
    public string IdempotencyKey { get; init; }

    /// <summary>
    /// Gets or sets the stable correlation id for the logical operation.
    /// </summary>
    public string CorrelationId { get; init; }

    /// <summary>
    /// Gets or sets the trace id for the current attempt.
    /// </summary>
    public string TraceId { get; init; }

    /// <summary>
    /// Gets or sets the attempt id for the current processing attempt.
    /// </summary>
    public string AttemptId { get; init; }

    /// <summary>
    /// Gets a value indicating whether at least one metadata value is available.
    /// </summary>
    public bool HasValues =>
        !string.IsNullOrWhiteSpace(IdempotencyKey) ||
        !string.IsNullOrWhiteSpace(CorrelationId) ||
        !string.IsNullOrWhiteSpace(TraceId) ||
        !string.IsNullOrWhiteSpace(AttemptId);

    /// <summary>
    /// Creates metadata from a SquirrelBox identity.
    /// </summary>
    /// <param name="identity">The SquirrelBox identity.</param>
    /// <returns>The metadata values for the identity.</returns>
    public static SquirrelBoxMetadata FromIdentity(SquirrelBoxIdentity identity)
    {
        if (identity is null)
            return Empty;

        return new SquirrelBoxMetadata
        {
            IdempotencyKey = identity.Operation?.IdempotencyKey?.Value,
            CorrelationId = identity.Operation?.CorrelationId?.Value,
            TraceId = identity.Attempt?.TraceId?.Value,
            AttemptId = identity.Attempt?.AttemptId?.Value
        };
    }
}
