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

    /// <summary>
    /// Gets the metadata key that stores the SquirrelBox outbox envelope ULID.
    /// </summary>
    public const string OutboxEnvelopeId = "squirrelbox-outbox-id";

    /// <summary>
    /// Gets the metadata key that stores the deferred policy name.
    /// </summary>
    public const string DeferredPolicyName = "squirrelbox-deferred-policy";

    /// <summary>
    /// Gets the metadata key that stores the deferred execution lane.
    /// </summary>
    public const string DeferredLane = "squirrelbox-deferred-lane";

    /// <summary>
    /// Gets the metadata key that stores the effective maximum execution attempts.
    /// </summary>
    public const string DeferredMaxAttempts = "squirrelbox-deferred-max-attempts";

    /// <summary>
    /// Gets the metadata key that stores the deferred retry delay in milliseconds.
    /// </summary>
    public const string DeferredRetryDelayMilliseconds = "squirrelbox-deferred-retry-delay-ms";

    /// <summary>
    /// Gets the metadata key that stores the deferred retry maximum delay in milliseconds.
    /// </summary>
    public const string DeferredRetryMaxDelayMilliseconds = "squirrelbox-deferred-retry-max-delay-ms";

    /// <summary>
    /// Gets the metadata key that stores the deferred retry backoff strategy.
    /// </summary>
    public const string DeferredRetryBackoff = "squirrelbox-deferred-retry-backoff";

    /// <summary>
    /// Gets the metadata key that stores the deferred retry jitter ratio.
    /// </summary>
    public const string DeferredRetryJitterRatio = "squirrelbox-deferred-retry-jitter-ratio";
}
