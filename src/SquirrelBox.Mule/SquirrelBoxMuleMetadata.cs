namespace SquirrelBox.Mule;

/// <summary>
/// Defines metadata keys used by SquirrelBox when scheduling Mule durable actions.
/// </summary>
public static class SquirrelBoxMuleMetadata
{
    /// <summary>
    /// Gets the metadata key that stores the SquirrelBox inbox entry ULID.
    /// </summary>
    public const string InboxEntryId = "squirrelbox-inbox-id";

    /// <summary>
    /// Gets the metadata key that stores the effective idempotency key.
    /// </summary>
    public const string IdempotencyKey = "idempotency-key";
}
