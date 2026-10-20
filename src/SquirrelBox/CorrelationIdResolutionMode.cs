namespace SquirrelBox;

/// <summary>
/// Defines how SquirrelBox resolves the correlation id for an inbox operation.
/// </summary>
public enum CorrelationIdResolutionMode
{
    /// <summary>
    /// Uses the next configured scope, falling back to the global default.
    /// </summary>
    Default,

    /// <summary>
    /// Uses an incoming correlation id when present and otherwise creates one with the configured factory.
    /// </summary>
    FactoryOnly,

    /// <summary>
    /// Tries registered resolution strategies before creating a new correlation id with the configured factory.
    /// </summary>
    ResolveThenGenerate,

    /// <summary>
    /// Requires an incoming correlation id or a successful strategy result and never generates a fallback value.
    /// </summary>
    ResolveRequired
}
