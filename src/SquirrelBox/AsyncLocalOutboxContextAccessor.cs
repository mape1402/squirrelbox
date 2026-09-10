namespace SquirrelBox;

/// <summary>
/// AsyncLocal-backed implementation of <see cref="IOutboxContextAccessor"/>.
/// </summary>
public sealed class AsyncLocalOutboxContextAccessor : IOutboxContextAccessor
{
    private readonly AsyncLocal<OutboxContext> _current = new();

    /// <inheritdoc />
    public OutboxContext Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
