namespace SquirrelBox;

/// <summary>
/// Represents a persisted inbox reservation and its execution state.
/// </summary>
public sealed class InboxEntry
{
    /// <summary>
    /// Gets or sets the ULID that identifies the inbox entry.
    /// </summary>
    public Ulid Id { get; set; }

    /// <summary>
    /// Gets or sets the source transport or component.
    /// </summary>
    public string Source { get; set; }

    /// <summary>
    /// Gets or sets the protected operation name.
    /// </summary>
    public string Operation { get; set; }

    /// <summary>
    /// Gets or sets the effective idempotency key.
    /// </summary>
    public string IdempotencyKey { get; set; }

    /// <summary>
    /// Gets or sets how the idempotency key was obtained.
    /// </summary>
    public InboxIdempotencyKeySource IdempotencyKeySource { get; set; }

    /// <summary>
    /// Gets or sets the semantic payload hash.
    /// </summary>
    public string PayloadHash { get; set; }

    /// <summary>
    /// Gets or sets the payload type name.
    /// </summary>
    public string PayloadType { get; set; }

    /// <summary>
    /// Gets or sets the correlation id.
    /// </summary>
    public string CorrelationId { get; set; }

    /// <summary>
    /// Gets or sets the current inbox status.
    /// </summary>
    public InboxStatus Status { get; set; }

    /// <summary>
    /// Gets or sets whether execution is inline or deferred.
    /// </summary>
    public InboxExecutionMode ExecutionMode { get; set; }

    /// <summary>
    /// Gets or sets when the entry was created in UTC.
    /// </summary>
    public DateTimeOffset CreatedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets when the entry was last updated in UTC.
    /// </summary>
    public DateTimeOffset UpdatedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets when the entry completed in UTC.
    /// </summary>
    public DateTimeOffset? CompletedOnUtc { get; set; }

    /// <summary>
    /// Gets or sets when the entry expires in UTC.
    /// </summary>
    public DateTimeOffset? ExpiresOnUtc { get; set; }

    /// <summary>
    /// Gets or sets a plain failure description.
    /// </summary>
    public string Failure { get; set; }

    /// <summary>
    /// Gets or sets completion details.
    /// </summary>
    public InboxCompletion Completion { get; set; }

    /// <summary>
    /// Gets or sets structured failure details.
    /// </summary>
    public InboxFailure FailureDetails { get; set; }

    /// <summary>
    /// Gets or sets additional inbox metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
