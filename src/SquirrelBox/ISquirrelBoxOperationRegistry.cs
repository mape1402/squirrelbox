namespace SquirrelBox;

/// <summary>
/// Resolves declared operations by request and result type.
/// </summary>
public interface ISquirrelBoxOperationRegistry
{
    /// <summary>
    /// Resolves the unique operation type for a request and result pair.
    /// </summary>
    /// <param name="requestType">The request type.</param>
    /// <param name="resultType">The result type.</param>
    /// <returns>The operation type.</returns>
    Type Resolve(Type requestType, Type resultType);
}
