namespace SquirrelBox;

public sealed class InboxOpenRequest
{
    public string Source { get; init; }

    public string Operation { get; init; }

    public string IdempotencyKey { get; init; }

    public object Payload { get; init; }

    public string PayloadType { get; init; }

    public string CorrelationId { get; init; }

    public string Owner { get; init; }

    public bool OwnsCompletion { get; init; } = true;

    public bool? AllowPayloadHashAsIdempotencyKey { get; init; }

    public InboxExecutionMode? ExecutionMode { get; init; }

    public DateTimeOffset? ExpiresOnUtc { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static InboxOpenRequest For<TPayload>(
        string source,
        string operation,
        string idempotencyKey = null,
        TPayload payload = default,
        string correlationId = null,
        string owner = null,
        bool ownsCompletion = true,
        bool? allowPayloadHashAsIdempotencyKey = null,
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
            Owner = owner,
            OwnsCompletion = ownsCompletion,
            AllowPayloadHashAsIdempotencyKey = allowPayloadHashAsIdempotencyKey,
            ExecutionMode = executionMode,
            ExpiresOnUtc = expiresOnUtc
        };
}
