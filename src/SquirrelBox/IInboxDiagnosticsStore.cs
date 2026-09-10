namespace SquirrelBox;

/// <summary>
/// Provides persisted inbox entries for diagnostics and dashboard history.
/// </summary>
public interface IInboxDiagnosticsStore
{
    /// <summary>
    /// Queries inbox entries for diagnostics.
    /// </summary>
    /// <param name="query">The diagnostics query.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The matching inbox entries.</returns>
    ValueTask<IReadOnlyList<InboxEntry>> QueryAsync(
        InboxQuery query,
        CancellationToken cancellationToken = default);
}
