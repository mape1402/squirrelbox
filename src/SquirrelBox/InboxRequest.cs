namespace SquirrelBox;

public sealed class InboxRequest
{
    public string Source { get; init; }

    public string Operation { get; init; }

    public string IdempotencyKey { get; init; }

    public object Payload { get; init; }

    public string PayloadType { get; init; }

    public string CorrelationId { get; init; }

    public InboxExecutionMode? ExecutionMode { get; init; }

    public DateTimeOffset? ExpiresOnUtc { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static InboxRequest For<TPayload>(
        string source,
        string operation,
        string idempotencyKey,
        TPayload payload,
        string correlationId = null,
        InboxExecutionMode? executionMode = null,
        DateTimeOffset? expiresOnUtc = null)
        => new()
        {
            Source = source,
            Operation = operation,
            IdempotencyKey = idempotencyKey,
            Payload = payload,
            PayloadType = typeof(TPayload).AssemblyQualifiedName,
            CorrelationId = correlationId,
            ExecutionMode = executionMode,
            ExpiresOnUtc = expiresOnUtc
        };
}
