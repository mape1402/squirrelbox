namespace SquirrelBox.AspNetCore.Dashboard;

/// <summary>
/// Authenticates users for the SquirrelBox dashboard when custom authentication is enabled.
/// </summary>
public interface ISquirrelBoxDashboardAuthenticator
{
    /// <summary>
    /// Authenticates the current dashboard request.
    /// </summary>
    /// <param name="request">The authentication request.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The authentication result.</returns>
    Task<SquirrelBoxDashboardAuthResult> AuthenticateAsync(
        SquirrelBoxDashboardAuthRequest request,
        CancellationToken cancellationToken);
}
