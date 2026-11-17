namespace SquirrelBox.AspNetCore;

/// <summary>
/// Provides optional incoming identity handling for an ASP.NET Core inbox entrypoint.
/// </summary>
public interface ISquirrelBoxInboxIdentityMetadata
{
    /// <summary>
    /// Gets how the effective idempotency key is selected by the entrypoint.
    /// </summary>
    InboxIdempotencyKeyResolutionMode IdempotencyKeyResolutionMode { get; }

    /// <summary>
    /// Gets whether the entrypoint accepts an incoming correlation id.
    /// </summary>
    InboxCorrelationIdInputMode CorrelationIdInputMode { get; }
}
