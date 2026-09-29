namespace SquirrelBox;

/// <summary>
/// Creates trace ids for individual SquirrelBox attempts.
/// </summary>
public interface ITraceIdFactory
{
    /// <summary>
    /// Creates a trace id for the supplied identity context.
    /// </summary>
    string Create(InboxIdentityFactoryContext context);
}
