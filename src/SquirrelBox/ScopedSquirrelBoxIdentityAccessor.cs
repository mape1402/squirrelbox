namespace SquirrelBox;

/// <summary>
/// Scoped implementation of <see cref="ISquirrelBoxIdentityAccessor"/> for request and message processing scopes.
/// </summary>
public sealed class ScopedSquirrelBoxIdentityAccessor : ISquirrelBoxIdentityAccessor
{
    private SquirrelBoxIdentity _current;

    /// <inheritdoc />
    public void Prepare()
    {
    }

    /// <inheritdoc />
    public SquirrelBoxIdentity Current
    {
        get => _current;
        set => _current = value;
    }
}
