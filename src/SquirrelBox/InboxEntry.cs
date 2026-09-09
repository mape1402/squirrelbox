namespace SquirrelBox;

public sealed class InboxEntry
{
    public Guid Id { get; set; }

    public string Source { get; set; }

    public string Operation { get; set; }

    public string IdempotencyKey { get; set; }

    public string PayloadHash { get; set; }

    public string PayloadType { get; set; }

    public string CorrelationId { get; set; }

    public InboxStatus Status { get; set; }

    public InboxExecutionMode ExecutionMode { get; set; }

    public DateTimeOffset CreatedOnUtc { get; set; }

    public DateTimeOffset UpdatedOnUtc { get; set; }

    public DateTimeOffset? CompletedOnUtc { get; set; }

    public DateTimeOffset? ExpiresOnUtc { get; set; }

    public string Failure { get; set; }
}
