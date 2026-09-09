using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SquirrelBox;

public sealed class JsonInboxPayloadHasher : IInboxPayloadHasher
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public string ComputeHash(object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        var json = JsonSerializer.Serialize(payload, payload.GetType(), Options);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
