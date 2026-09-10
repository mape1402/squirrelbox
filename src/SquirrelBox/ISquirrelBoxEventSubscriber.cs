namespace SquirrelBox;

/// <summary>
/// Subscribes to live SquirrelBox observability events.
/// </summary>
public interface ISquirrelBoxEventSubscriber
{
    /// <summary>
    /// Subscribes a handler to future events.
    /// </summary>
    /// <param name="handler">The event handler.</param>
    /// <returns>A disposable subscription.</returns>
    IDisposable Subscribe(Func<SquirrelBoxEvent, CancellationToken, ValueTask> handler);
}
