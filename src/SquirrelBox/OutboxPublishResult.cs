namespace SquirrelBox;

/// <summary>
/// Represents the result returned after an outbox envelope is published by a transport adapter.
/// </summary>
public sealed class OutboxPublishResult
{
    /// <summary>
    /// Gets a successful publication result.
    /// </summary>
    public static OutboxPublishResult Success { get; } = new(true);

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxPublishResult"/> class.
    /// </summary>
    /// <param name="succeeded">Whether publication succeeded.</param>
    /// <param name="failure">Optional failure details.</param>
    public OutboxPublishResult(bool succeeded, OutboxFailure failure = null)
    {
        Succeeded = succeeded;
        Failure = failure;
    }

    /// <summary>
    /// Gets a value indicating whether publication succeeded.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// Gets the failure details when publication did not succeed.
    /// </summary>
    public OutboxFailure Failure { get; }

    /// <summary>
    /// Creates a failed publication result.
    /// </summary>
    /// <param name="failure">The failure details.</param>
    /// <returns>The failed publication result.</returns>
    public static OutboxPublishResult Failed(OutboxFailure failure)
        => new(false, failure ?? throw new ArgumentNullException(nameof(failure)));
}
