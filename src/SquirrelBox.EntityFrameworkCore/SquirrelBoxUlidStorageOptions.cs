using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SquirrelBox.EntityFrameworkCore;

/// <summary>
/// Configures how SquirrelBox ULID columns are mapped by Entity Framework Core.
/// </summary>
public sealed class SquirrelBoxUlidStorageOptions
{
    /// <summary>
    /// The canonical ULID text length.
    /// </summary>
    public const int TextLength = 26;

    /// <summary>
    /// The canonical ULID binary length.
    /// </summary>
    public const int BinaryLength = 16;

    /// <summary>
    /// Gets or sets the ULID storage mode.
    /// </summary>
    public SquirrelBoxUlidStorageMode Mode { get; set; } = SquirrelBoxUlidStorageMode.Text;

    /// <summary>
    /// Gets or sets the provider column type to use for ULID properties.
    /// </summary>
    public string ColumnType { get; set; }

    /// <summary>
    /// Gets the custom ULID property configuration callbacks applied after SquirrelBox defaults.
    /// </summary>
    internal IList<Action<PropertyBuilder<Ulid>>> PropertyConfigurators { get; } = [];
}
