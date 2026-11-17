namespace SquirrelBox;

/// <summary>
/// Defines whether an inbox operation accepts an incoming correlation id.
/// </summary>
public enum InboxCorrelationIdInputMode
{
    /// <summary>
    /// Uses the next configured scope, falling back to the global default.
    /// </summary>
    Default,

    /// <summary>
    /// Preserves an incoming correlation id and only resolves or generates a value when it is missing.
    /// </summary>
    PreferIncoming,

    /// <summary>
    /// Ignores an incoming correlation id so configured strategies or the factory determine the effective value.
    /// </summary>
    IgnoreIncoming
}
