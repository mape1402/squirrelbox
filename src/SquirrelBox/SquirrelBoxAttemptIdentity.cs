namespace SquirrelBox;

/// <summary>
/// Represents the identity assigned to one SquirrelBox processing attempt.
/// </summary>
public sealed class SquirrelBoxAttemptIdentity
{
    /// <summary>
    /// Gets or sets the SquirrelBox attempt id.
    /// </summary>
    public SquirrelBoxMetadataValue AttemptId { get; set; }

    /// <summary>
    /// Gets or sets the trace id for this attempt.
    /// </summary>
    public SquirrelBoxMetadataValue TraceId { get; set; }
}
