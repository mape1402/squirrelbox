namespace SquirrelBox;

public sealed class InboxCompletion
{
    public static readonly InboxCompletion Empty = new();

    public string ResultType { get; init; }

    public string ContentType { get; init; }

    public byte[] ResultPayload { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
