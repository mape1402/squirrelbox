namespace SquirrelBox;

/// <summary>
/// Describes a durable SquirrelBox operation that can execute a request.
/// </summary>
public interface ISquirrelBoxOperation
{
    /// <summary>
    /// Gets the request type handled by the operation.
    /// </summary>
    Type RequestType { get; }

    /// <summary>
    /// Gets the result type produced by the operation.
    /// </summary>
    Type ResultType { get; }

    /// <summary>
    /// Executes the operation.
    /// </summary>
    /// <param name="request">The request payload.</param>
    /// <param name="context">The operation context.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The operation result boxed as an object.</returns>
    ValueTask<object> ExecuteAsync(
        object request,
        SquirrelBoxOperationContext context,
        CancellationToken cancellationToken = default);
}
