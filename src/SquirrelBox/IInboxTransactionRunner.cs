namespace SquirrelBox;

/// <summary>
/// Runs inbox storage work inside the transaction behavior required by the provider.
/// </summary>
public interface IInboxTransactionRunner
{
    /// <summary>
    /// Executes inbox storage work and returns its result.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="operation">The storage operation to run.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The operation result.</returns>
    ValueTask<T> RunAsync<T>(Func<CancellationToken, ValueTask<T>> operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes inbox storage work.
    /// </summary>
    /// <param name="operation">The storage operation to run.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the operation finishes.</returns>
    ValueTask RunAsync(Func<CancellationToken, ValueTask> operation, CancellationToken cancellationToken = default);
}
