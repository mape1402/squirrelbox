using Mule;

namespace SquirrelBox.Messaging.Pigeon;

/// <summary>
/// Defines Mule action keys used by SquirrelBox Pigeon continuations.
/// </summary>
public static class SquirrelBoxPigeonMuleActionKeys
{
    /// <summary>
    /// Gets the textual action key for deferred Pigeon consumers.
    /// </summary>
    public const string Consume = "squirrelbox.pigeon.consume.v1";

    /// <summary>
    /// Gets the Mule action key for deferred Pigeon consumers.
    /// </summary>
    public static readonly ActionKey ConsumeKey = ActionKey.From(Consume);
}
