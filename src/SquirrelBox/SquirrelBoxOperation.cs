namespace SquirrelBox;

/// <summary>
/// Base class for declared SquirrelBox operations.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResult">The result type.</typeparam>
public abstract class SquirrelBoxOperation<TRequest, TResult> : ISquirrelBoxOperation
{
    /// <inheritdoc />
    public Type RequestType => typeof(TRequest);

    /// <inheritdoc />
    public Type ResultType => typeof(TResult);

    /// <inheritdoc />
    async ValueTask<object> ISquirrelBoxOperation.ExecuteAsync(
        object request,
        SquirrelBoxOperationContext context,
        CancellationToken cancellationToken)
        => await ExecuteAsync((TRequest)request, context, cancellationToken);

    /// <summary>
    /// Executes the operation for the supplied request.
    /// </summary>
    /// <param name="request">The request payload.</param>
    /// <param name="context">The operation context.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The operation result.</returns>
    protected abstract ValueTask<TResult> ExecuteAsync(
        TRequest request,
        SquirrelBoxOperationContext context,
        CancellationToken cancellationToken);
}
