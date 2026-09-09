namespace SquirrelBox;

/// <summary>
/// Computes stable hashes for inbox payload fingerprints.
/// </summary>
public interface IInboxPayloadHasher
{
    /// <summary>
    /// Computes a hash for a payload.
    /// </summary>
    /// <param name="payload">The payload to hash.</param>
    /// <returns>A stable lowercase hash.</returns>
    string ComputeHash(object payload);
}
