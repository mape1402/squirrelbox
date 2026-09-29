namespace SquirrelBox;

/// <summary>
/// Provides access to the current SquirrelBox operation and attempt identity.
/// </summary>
public interface ISquirrelBoxIdentityAccessor
{
    /// <summary>
    /// Prepares the current async flow so later identity mutations remain visible to callers.
    /// </summary>
    void Prepare();

    /// <summary>
    /// Gets or sets the current SquirrelBox identity.
    /// </summary>
    SquirrelBoxIdentity Current { get; set; }
}
