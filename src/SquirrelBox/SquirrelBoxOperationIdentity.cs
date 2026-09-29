namespace SquirrelBox;

/// <summary>
/// Represents the stable identity assigned to a logical SquirrelBox operation.
/// </summary>
public sealed class SquirrelBoxOperationIdentity
{
    /// <summary>
    /// Gets or sets the effective idempotency key.
    /// </summary>
    public SquirrelBoxMetadataValue IdempotencyKey { get; set; }

    /// <summary>
    /// Gets or sets the stable correlation id for this logical operation.
    /// </summary>
    public SquirrelBoxMetadataValue CorrelationId { get; set; }
}
