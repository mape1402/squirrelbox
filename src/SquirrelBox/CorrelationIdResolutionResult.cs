namespace SquirrelBox;

/// <summary>
/// Describes the result of resolving a correlation id.
/// </summary>
public sealed class CorrelationIdResolutionResult
{
    private CorrelationIdResolutionResult(
        CorrelationIdResolutionState state,
        string correlationId,
        SquirrelBoxMetadataValueSource source,
        string resolverName)
    {
        State = state;
        CorrelationId = correlationId;
        Source = source;
        ResolverName = resolverName;
    }

    /// <summary>
    /// Gets a result indicating that a strategy does not apply to the current operation.
    /// </summary>
    public static CorrelationIdResolutionResult NotApplicable { get; } = new(
        CorrelationIdResolutionState.NotApplicable,
        null,
        SquirrelBoxMetadataValueSource.Missing,
        null);

    /// <summary>
    /// Gets a result indicating that a strategy could not find a correlation id.
    /// </summary>
    public static CorrelationIdResolutionResult NotFound { get; } = new(
        CorrelationIdResolutionState.NotFound,
        null,
        SquirrelBoxMetadataValueSource.Missing,
        null);

    /// <summary>
    /// Gets the resolution state.
    /// </summary>
    public CorrelationIdResolutionState State { get; }

    /// <summary>
    /// Gets the resolved correlation id, when available.
    /// </summary>
    public string CorrelationId { get; }

    /// <summary>
    /// Gets how the correlation id was obtained.
    /// </summary>
    public SquirrelBoxMetadataValueSource Source { get; }

    /// <summary>
    /// Gets the strategy or factory name that produced the value.
    /// </summary>
    public string ResolverName { get; }

    /// <summary>
    /// Creates a successful strategy result.
    /// </summary>
    /// <param name="correlationId">The resolved correlation id.</param>
    /// <returns>A successful resolution result.</returns>
    public static CorrelationIdResolutionResult Resolved(string correlationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return new CorrelationIdResolutionResult(
            CorrelationIdResolutionState.Resolved,
            correlationId,
            SquirrelBoxMetadataValueSource.Resolved,
            null);
    }

    internal static CorrelationIdResolutionResult FromFactory(
        string correlationId,
        SquirrelBoxMetadataValueSource source,
        string factoryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        return new CorrelationIdResolutionResult(
            CorrelationIdResolutionState.Resolved,
            correlationId,
            source,
            factoryName);
    }

    internal CorrelationIdResolutionResult WithResolverName(string resolverName)
        => new(State, CorrelationId, Source, resolverName);
}
