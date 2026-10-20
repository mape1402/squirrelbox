namespace SquirrelBox;

/// <summary>
/// Resolves correlation ids from operation-specific application state.
/// </summary>
public interface ICorrelationIdResolutionStrategy
{
    /// <summary>
    /// Gets the stable name used to identify the strategy in diagnostics and persisted metadata.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the strategy order. Lower values execute first.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Attempts to resolve a correlation id for the current inbox operation.
    /// </summary>
    /// <param name="context">The identity context for the operation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The strategy resolution result.</returns>
    ValueTask<CorrelationIdResolutionResult> TryResolveAsync(
        InboxIdentityFactoryContext context,
        CancellationToken cancellationToken = default);
}
