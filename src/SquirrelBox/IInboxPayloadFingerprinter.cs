namespace SquirrelBox;

/// <summary>
/// Resolves the object that should be hashed for a payload.
/// </summary>
public interface IInboxPayloadFingerprinter
{
    /// <summary>
    /// Creates the hash input for the supplied payload.
    /// </summary>
    /// <param name="payload">The original payload.</param>
    /// <returns>The semantic fingerprint, or the original payload when no profile applies.</returns>
    object CreateFingerprint(object payload);
}
