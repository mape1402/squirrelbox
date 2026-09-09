namespace SquirrelBox;

/// <summary>
/// Executes declared SquirrelBox operations through the inbox lifecycle.
/// </summary>
public interface ISquirrelBoxOperationService
{
    /// <summary>
    /// Executes a specific operation type for a request.
    /// </summary>
    /// <typeparam name="TOperation">The operation type.</typeparam>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="request">The request payload.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The operation invocation result.</returns>
    ValueTask<SquirrelBoxOperationResult<TResult>> ExecuteAsync<TOperation, TRequest, TResult>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TOperation : SquirrelBoxOperation<TRequest, TResult>;

    /// <summary>
    /// Executes the unique operation discovered for a request and result pair.
    /// </summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="request">The request payload.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The operation invocation result.</returns>
    ValueTask<SquirrelBoxOperationResult<TResult>> ExecuteAsync<TRequest, TResult>(
        TRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Continues a previously deferred operation envelope in the current inbox context.
    /// </summary>
    /// <param name="envelope">The durable operation envelope.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The operation result boxed as an object.</returns>
    ValueTask<object> ContinueAsync(
        SquirrelBoxOperationEnvelope envelope,
        CancellationToken cancellationToken = default);
}
