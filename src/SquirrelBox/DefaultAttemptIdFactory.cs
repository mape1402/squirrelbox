namespace SquirrelBox;

/// <summary>
/// Default attempt id factory that creates ULID-based attempt ids.
/// </summary>
public sealed class DefaultAttemptIdFactory : IAttemptIdFactory
{
    /// <inheritdoc />
    public string Create(InboxIdentityFactoryContext context)
        => Ulid.NewUlid().ToString();
}
