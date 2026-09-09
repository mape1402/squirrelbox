using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SquirrelBox;

/// <summary>
/// Computes SHA-256 hashes from JSON serialized payload fingerprints.
/// </summary>
public sealed class JsonInboxPayloadHasher : IInboxPayloadHasher
{
    private readonly IInboxPayloadFingerprinter _fingerprinter;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonInboxPayloadHasher"/> class.
    /// </summary>
    /// <param name="fingerprinter">The semantic payload fingerprinter.</param>
    public JsonInboxPayloadHasher(IInboxPayloadFingerprinter fingerprinter)
    {
        _fingerprinter = fingerprinter ?? throw new ArgumentNullException(nameof(fingerprinter));
    }

    /// <inheritdoc />
    public string ComputeHash(object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var fingerprint = _fingerprinter.CreateFingerprint(payload);
        var json = JsonSerializer.Serialize(fingerprint, fingerprint.GetType(), Options);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
