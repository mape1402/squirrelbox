namespace SquirrelBox;

/// <summary>
/// Creates attempt ids for individual SquirrelBox attempts.
/// </summary>
public interface IAttemptIdFactory
{
    /// <summary>
    /// Creates an attempt id for the supplied identity context.
    /// </summary>
    string Create(InboxIdentityFactoryContext context);
}
