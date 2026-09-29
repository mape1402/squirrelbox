namespace SquirrelBox;

/// <summary>
/// Creates stable correlation ids for logical SquirrelBox operations.
/// </summary>
public interface ICorrelationIdFactory
{
    /// <summary>
    /// Creates a correlation id for the supplied identity context.
    /// </summary>
    string Create(InboxIdentityFactoryContext context);
}
