using System.Threading;

namespace SquirrelBox;

/// <summary>
/// AsyncLocal implementation of <see cref="ISquirrelBoxIdentityAccessor"/>.
/// </summary>
public sealed class AsyncLocalSquirrelBoxIdentityAccessor : ISquirrelBoxIdentityAccessor
{
    private static readonly AsyncLocal<SquirrelBoxIdentityHolder> Holder = new();

    /// <inheritdoc />
    public void Prepare()
        => _ = PrepareHolder();

    /// <inheritdoc />
    public SquirrelBoxIdentity Current
    {
        get => Holder.Value?.Identity;
        set => PrepareHolder().Identity = value;
    }

    private static SquirrelBoxIdentityHolder PrepareHolder()
    {
        if (Holder.Value is null)
            Holder.Value = new SquirrelBoxIdentityHolder();

        return Holder.Value;
    }

    private sealed class SquirrelBoxIdentityHolder
    {
        public SquirrelBoxIdentity Identity { get; set; }
    }
}
