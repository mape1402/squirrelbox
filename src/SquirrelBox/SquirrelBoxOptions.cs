namespace SquirrelBox;

public sealed class SquirrelBoxOptions
{
    public InboxExecutionMode DefaultExecutionMode { get; set; } = InboxExecutionMode.Inline;

    public TimeSpan? DefaultEntryLifetime { get; set; }
}
