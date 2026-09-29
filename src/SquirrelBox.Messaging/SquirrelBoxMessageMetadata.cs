namespace SquirrelBox.Messaging;

/// <summary>
/// Represents SquirrelBox identity metadata that can be propagated through messaging transports.
/// </summary>
public sealed class SquirrelBoxMessageMetadata
{
    /// <summary>
    /// Gets an empty metadata model.
    /// </summary>
    public static SquirrelBoxMessageMetadata Empty { get; } = new();

    /// <summary>
    /// Gets the effective operation idempotency key metadata value.
    /// </summary>
    public SquirrelBoxMetadataValue IdempotencyKey { get; init; }

    /// <summary>
    /// Gets the stable operation correlation id metadata value.
    /// </summary>
    public SquirrelBoxMetadataValue CorrelationId { get; init; }

    /// <summary>
    /// Gets the per-attempt attempt id metadata value.
    /// </summary>
    public SquirrelBoxMetadataValue AttemptId { get; init; }

    /// <summary>
    /// Gets the per-attempt trace id metadata value.
    /// </summary>
    public SquirrelBoxMetadataValue TraceId { get; init; }

    /// <summary>
    /// Gets the SquirrelBox identity used to create this metadata model.
    /// </summary>
    public SquirrelBoxIdentity Identity { get; init; }

    /// <summary>
    /// Gets a value indicating whether at least one metadata value is available.
    /// </summary>
    public bool HasValues =>
        HasValue(IdempotencyKey) ||
        HasValue(CorrelationId) ||
        HasValue(AttemptId) ||
        HasValue(TraceId);

    /// <summary>
    /// Writes the available metadata values into the target dictionary.
    /// </summary>
    /// <param name="target">The metadata dictionary to enrich.</param>
    /// <param name="overwrite">Whether existing values with the same names should be replaced.</param>
    public void WriteTo(IDictionary<string, string> target, bool overwrite = true)
    {
        ArgumentNullException.ThrowIfNull(target);

        Write(target, IdempotencyKey, overwrite);
        Write(target, CorrelationId, overwrite);
        Write(target, AttemptId, overwrite);
        Write(target, TraceId, overwrite);
    }

    /// <summary>
    /// Creates a message metadata model from a SquirrelBox identity.
    /// </summary>
    /// <param name="identity">The SquirrelBox identity.</param>
    /// <returns>The corresponding message metadata model.</returns>
    public static SquirrelBoxMessageMetadata FromIdentity(SquirrelBoxIdentity identity)
    {
        if (identity is null)
            return Empty;

        return new SquirrelBoxMessageMetadata
        {
            Identity = identity,
            IdempotencyKey = identity.Operation?.IdempotencyKey,
            CorrelationId = identity.Operation?.CorrelationId,
            AttemptId = identity.Attempt?.AttemptId,
            TraceId = identity.Attempt?.TraceId
        };
    }

    private static bool HasValue(SquirrelBoxMetadataValue value)
        => !string.IsNullOrWhiteSpace(value?.Name) &&
           !string.IsNullOrWhiteSpace(value.Value);

    private static void Write(
        IDictionary<string, string> target,
        SquirrelBoxMetadataValue value,
        bool overwrite)
    {
        if (!HasValue(value))
            return;

        if (overwrite || !target.ContainsKey(value.Name))
            target[value.Name] = value.Value;
    }
}
