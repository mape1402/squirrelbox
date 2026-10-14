namespace SquirrelBox;

/// <summary>
/// Converts outbox payload CLR types to stable persisted names and resolves them back when replaying durable envelopes.
/// </summary>
public interface IOutboxPayloadTypeNameResolver
{
    /// <summary>
    /// Gets the persisted payload type name for the specified CLR type.
    /// </summary>
    /// <param name="payloadType">The CLR payload type.</param>
    /// <returns>The persisted payload type name.</returns>
    string GetName(Type payloadType);

    /// <summary>
    /// Resolves a persisted payload type name to a CLR type.
    /// </summary>
    /// <param name="payloadTypeName">The persisted payload type name.</param>
    /// <returns>The resolved CLR type.</returns>
    Type Resolve(string payloadTypeName);
}
