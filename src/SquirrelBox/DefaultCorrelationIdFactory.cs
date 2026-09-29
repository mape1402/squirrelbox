namespace SquirrelBox;

/// <summary>
/// Default correlation id factory that reuses incoming values or generates ULIDs.
/// </summary>
public sealed class DefaultCorrelationIdFactory : ICorrelationIdFactory
{
    /// <inheritdoc />
    public string Create(InboxIdentityFactoryContext context)
        => !string.IsNullOrWhiteSpace(context?.IncomingCorrelationId)
            ? context.IncomingCorrelationId
            : Ulid.NewUlid().ToString();
}
