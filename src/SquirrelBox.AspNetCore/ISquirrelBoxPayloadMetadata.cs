namespace SquirrelBox.AspNetCore;

/// <summary>
/// Marks an ASP.NET Core endpoint whose SquirrelBox idempotency payload comes from a bound action argument.
/// </summary>
public interface ISquirrelBoxPayloadMetadata
{
    /// <summary>
    /// Gets the endpoint argument name that contains the payload used for idempotency hashing.
    /// </summary>
    string ArgumentName { get; }
}
