using System.Transactions;

namespace SquirrelBox;

/// <summary>
/// Runs inbox storage operations while suppressing any ambient business transaction.
/// </summary>
public sealed class SuppressAmbientTransactionInboxRunner : IInboxTransactionRunner
{
    /// <inheritdoc />
    public async ValueTask<T> RunAsync<T>(
        Func<CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using var scope = new TransactionScope(
            TransactionScopeOption.Suppress,
            TransactionScopeAsyncFlowOption.Enabled);

        var result = await operation(cancellationToken);
        scope.Complete();
        return result;
    }

    /// <inheritdoc />
    public async ValueTask RunAsync(
        Func<CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        using var scope = new TransactionScope(
            TransactionScopeOption.Suppress,
            TransactionScopeAsyncFlowOption.Enabled);

        await operation(cancellationToken);
        scope.Complete();
    }
}
