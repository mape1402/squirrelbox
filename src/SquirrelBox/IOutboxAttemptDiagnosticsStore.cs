namespace SquirrelBox;

/// <summary>
/// Queries outbox publication attempts for diagnostics and dashboards.
/// </summary>
public interface IOutboxAttemptDiagnosticsStore
{
    /// <summary>
    /// Queries outbox publication attempts.
    /// </summary>
    /// <param name="query">The diagnostics query.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching outbox attempts.</returns>
    ValueTask<IReadOnlyList<OutboxAttempt>> QueryAttemptsAsync(
        OutboxAttemptQuery query,
        CancellationToken cancellationToken = default);
}
