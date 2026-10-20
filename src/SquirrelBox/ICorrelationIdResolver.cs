namespace SquirrelBox;

/// <summary>
/// Coordinates incoming values, application strategies, and the terminal correlation id factory.
/// </summary>
public interface ICorrelationIdResolver
{
    /// <summary>
    /// Resolves the correlation id for an inbox operation.
    /// </summary>
    /// <param name="context">The identity context for the operation.</param>
    /// <param name="mode">The resolution mode selected by the entrypoint.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The correlation id resolution result.</returns>
    ValueTask<CorrelationIdResolutionResult> ResolveAsync(
        InboxIdentityFactoryContext context,
        CorrelationIdResolutionMode mode,
        CancellationToken cancellationToken = default);
}
