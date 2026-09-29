namespace SquirrelBox;

/// <summary>
/// Represents the current SquirrelBox operation and attempt identity.
/// </summary>
public sealed class SquirrelBoxIdentity
{
    /// <summary>
    /// Gets or sets the stable operation identity.
    /// </summary>
    public SquirrelBoxOperationIdentity Operation { get; set; } = new();

    /// <summary>
    /// Gets or sets the current attempt identity.
    /// </summary>
    public SquirrelBoxAttemptIdentity Attempt { get; set; } = new();
}
