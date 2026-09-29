namespace SquirrelBox.Messaging;

/// <summary>
/// Creates and propagates SquirrelBox messaging metadata from the current inbox identity.
/// </summary>
public interface ISquirrelBoxMessageMetadataEnricher
{
    /// <summary>
    /// Creates message metadata from the supplied SquirrelBox identity.
    /// </summary>
    /// <param name="identity">The SquirrelBox identity.</param>
    /// <returns>The corresponding message metadata model.</returns>
    SquirrelBoxMessageMetadata Create(SquirrelBoxIdentity identity);

    /// <summary>
    /// Creates message metadata from the current SquirrelBox identity.
    /// </summary>
    /// <returns>The current message metadata model.</returns>
    SquirrelBoxMessageMetadata CreateCurrent();

    /// <summary>
    /// Writes identity metadata into the target metadata dictionary.
    /// </summary>
    /// <param name="target">The target metadata dictionary.</param>
    /// <param name="identity">The identity to propagate.</param>
    void Enrich(IDictionary<string, string> target, SquirrelBoxIdentity identity);

    /// <summary>
    /// Writes the current identity metadata into the target metadata dictionary.
    /// </summary>
    /// <param name="target">The target metadata dictionary.</param>
    void EnrichCurrent(IDictionary<string, string> target);
}
