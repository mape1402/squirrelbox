namespace SquirrelBox.EntityFrameworkCore;

/// <summary>
/// Defines how SquirrelBox ULID values are stored by Entity Framework Core.
/// </summary>
public enum SquirrelBoxUlidStorageMode
{
    /// <summary>
    /// Stores ULID values as their canonical 26-character text representation.
    /// </summary>
    Text,

    /// <summary>
    /// Stores ULID values as 16-byte binary values.
    /// </summary>
    Binary16
}
