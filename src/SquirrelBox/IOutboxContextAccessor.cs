namespace SquirrelBox;

/// <summary>
/// Stores the ambient outbox context for the current asynchronous flow.
/// </summary>
public interface IOutboxContextAccessor
{
    /// <summary>
    /// Gets or sets the current outbox context.
    /// </summary>
    OutboxContext Current { get; set; }
}
