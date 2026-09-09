namespace SquirrelBox;

public sealed class SquirrelBoxOptions
{
    public InboxExecutionMode DefaultExecutionMode { get; set; } = InboxExecutionMode.Inline;

    public TimeSpan? DefaultEntryLifetime { get; set; }

    public bool AllowPayloadHashAsIdempotencyKey { get; set; } = true;

    public string DefaultOwner { get; set; } = "manual";
}
