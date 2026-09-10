using Mule;

namespace SquirrelBox.Mule;

/// <summary>
/// Defines Mule action keys used by SquirrelBox outbox publication.
/// </summary>
public static class SquirrelBoxOutboxMuleActionKeys
{
    /// <summary>
    /// Gets the textual action key for outbox publication.
    /// </summary>
    public const string Publish = "squirrelbox.outbox.publish.v1";

    /// <summary>
    /// Gets the Mule action key for outbox publication.
    /// </summary>
    public static readonly ActionKey PublishKey = ActionKey.From(Publish);
}
