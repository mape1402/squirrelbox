namespace SquirrelBox;

/// <summary>
/// Represents a resolved SquirrelBox metadata value and the name used to propagate it.
/// </summary>
public sealed class SquirrelBoxMetadataValue
{
    /// <summary>
    /// Gets or sets the metadata/header name used for this value.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets the metadata value.
    /// </summary>
    public string Value { get; set; }

    /// <summary>
    /// Gets or sets how the value was resolved.
    /// </summary>
    public SquirrelBoxMetadataValueSource Source { get; set; }

    /// <summary>
    /// Creates a resolved metadata value.
    /// </summary>
    public static SquirrelBoxMetadataValue Create(
        string name,
        string value,
        SquirrelBoxMetadataValueSource source)
        => new()
        {
            Name = name,
            Value = value,
            Source = source
        };
}
