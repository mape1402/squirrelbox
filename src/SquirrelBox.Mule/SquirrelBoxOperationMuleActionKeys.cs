using Mule;

namespace SquirrelBox.Mule;

/// <summary>
/// Defines Mule action keys used by SquirrelBox operation continuations.
/// </summary>
public static class SquirrelBoxOperationMuleActionKeys
{
    /// <summary>
    /// Gets the textual action key for deferred SquirrelBox operations.
    /// </summary>
    public const string Operation = "squirrelbox.operation.v1";

    /// <summary>
    /// Gets the Mule action key for deferred SquirrelBox operations.
    /// </summary>
    public static readonly ActionKey OperationKey = ActionKey.From(Operation);
}
