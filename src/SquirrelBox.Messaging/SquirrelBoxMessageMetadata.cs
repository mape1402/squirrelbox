using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquirrelBox.Messaging;

/// <summary>
/// Represents the structured SquirrelBox metadata section propagated through messaging transports.
/// </summary>
public sealed class SquirrelBoxMessageMetadata
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Gets an empty metadata model.
    /// </summary>
    public static SquirrelBoxMessageMetadata Empty { get; } = new()
    {
        Metadata = SquirrelBoxMetadata.Empty
    };

    /// <summary>
    /// Gets the structured SquirrelBox metadata payload.
    /// </summary>
    public SquirrelBoxMetadata Metadata { get; init; } = SquirrelBoxMetadata.Empty;

    /// <summary>
    /// Gets the effective operation idempotency key.
    /// </summary>
    public string IdempotencyKey => Metadata?.IdempotencyKey;

    /// <summary>
    /// Gets the stable operation correlation id.
    /// </summary>
    public string CorrelationId => Metadata?.CorrelationId;

    /// <summary>
    /// Gets the per-attempt attempt id.
    /// </summary>
    public string AttemptId => Metadata?.AttemptId;

    /// <summary>
    /// Gets the per-attempt trace id.
    /// </summary>
    public string TraceId => Metadata?.TraceId;

    /// <summary>
    /// Gets the SquirrelBox identity used to create this metadata model.
    /// </summary>
    public SquirrelBoxIdentity Identity { get; init; }

    /// <summary>
    /// Gets a value indicating whether at least one metadata value is available.
    /// </summary>
    public bool HasValues => Metadata?.HasValues == true;

    /// <summary>
    /// Writes the available metadata values as a single structured section into the target dictionary.
    /// </summary>
    /// <param name="target">The metadata dictionary to enrich.</param>
    /// <param name="overwrite">Whether an existing metadata section should be replaced.</param>
    public void WriteTo(IDictionary<string, string> target, bool overwrite = true)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!HasValues)
            return;

        if (!overwrite && target.ContainsKey(SquirrelBoxMetadataNames.MetadataSection))
            return;

        target[SquirrelBoxMetadataNames.MetadataSection] = JsonSerializer.Serialize(Metadata, JsonOptions);
    }

    /// <summary>
    /// Reads the structured SquirrelBox metadata section from a messaging metadata dictionary.
    /// </summary>
    /// <param name="source">The source metadata values.</param>
    /// <param name="metadata">The parsed metadata when the section exists and is valid.</param>
    /// <returns><see langword="true"/> when structured SquirrelBox metadata was found; otherwise, <see langword="false"/>.</returns>
    public static bool TryReadFrom(
        IEnumerable<KeyValuePair<string, string>> source,
        out SquirrelBoxMessageMetadata metadata)
    {
        metadata = Empty;

        var raw = ResolveSection(source);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        SquirrelBoxMetadata parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<SquirrelBoxMetadata>(raw, JsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (parsed?.HasValues != true)
            return false;

        metadata = new SquirrelBoxMessageMetadata
        {
            Metadata = parsed
        };
        return true;
    }

    /// <summary>
    /// Creates a message metadata model from a SquirrelBox identity.
    /// </summary>
    /// <param name="identity">The SquirrelBox identity.</param>
    /// <returns>The corresponding message metadata model.</returns>
    public static SquirrelBoxMessageMetadata FromIdentity(SquirrelBoxIdentity identity)
    {
        if (identity is null)
            return Empty;

        return new SquirrelBoxMessageMetadata
        {
            Identity = identity,
            Metadata = SquirrelBoxMetadata.FromIdentity(identity)
        };
    }

    private static string ResolveSection(IEnumerable<KeyValuePair<string, string>> source)
    {
        if (source is null)
            return null;

        foreach (var item in source)
        {
            if (string.Equals(
                    item.Key,
                    SquirrelBoxMetadataNames.MetadataSection,
                    StringComparison.OrdinalIgnoreCase))
            {
                return item.Value;
            }
        }

        return null;
    }
}
