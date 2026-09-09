using System.Threading;

namespace SquirrelBox;

/// <summary>
/// Stores the ambient inbox context in an <see cref="AsyncLocal{T}"/>.
/// </summary>
public sealed class AsyncLocalInboxContextAccessor : IInboxContextAccessor
{
    private static readonly AsyncLocal<InboxContextHolder> Holder = new();

    /// <inheritdoc />
    public InboxContext Current
    {
        get => Holder.Value?.Context;
        set => PrepareHolder().Context = value;
    }

    /// <inheritdoc />
    public void Prepare()
        => _ = PrepareHolder();

    private static InboxContextHolder PrepareHolder()
    {
        if (Holder.Value is null)
            Holder.Value = new InboxContextHolder();

        return Holder.Value;
    }

    private sealed class InboxContextHolder
    {
        public InboxContext Context { get; set; }
    }
}
