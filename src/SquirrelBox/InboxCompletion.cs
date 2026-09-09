namespace SquirrelBox;

/// <summary>
/// Stores optional completion details for a completed inbox entry.
/// </summary>
public sealed class InboxCompletion
{
    /// <summary>
    /// Gets an empty completion instance.
    /// </summary>
    public static readonly InboxCompletion Empty = new();

    /// <summary>
    /// Gets the CLR or logical result type.
    /// </summary>
    public string ResultType { get; init; }

    /// <summary>
    /// Gets the result content type.
    /// </summary>
    public string ContentType { get; init; }

    /// <summary>
    /// Gets the transport status code associated with the completion, when replayable.
    /// </summary>
    public int? StatusCode { get; init; }

    /// <summary>
    /// Gets the serialized result payload, when stored for replay.
    /// </summary>
    public byte[] ResultPayload { get; init; }

    /// <summary>
    /// Gets transport headers associated with the completion.
    /// </summary>
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets completion metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
