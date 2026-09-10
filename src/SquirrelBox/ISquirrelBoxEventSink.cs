namespace SquirrelBox;

/// <summary>
/// Combines publishing, subscription, and recent event buffering for SquirrelBox events.
/// </summary>
public interface ISquirrelBoxEventSink : ISquirrelBoxEventPublisher, ISquirrelBoxEventSubscriber
{
    /// <summary>
    /// Gets a snapshot of recently published events.
    /// </summary>
    /// <param name="limit">The maximum number of events to return.</param>
    /// <returns>The recent event snapshot.</returns>
    IReadOnlyList<SquirrelBoxEvent> GetRecent(int limit = 100);
}
