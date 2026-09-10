namespace SquirrelBox.AspNetCore.Dashboard;

/// <summary>
/// Defines how the SquirrelBox dashboard authenticates users.
/// </summary>
public enum SquirrelBoxDashboardAuthenticationMode
{
    /// <summary>
    /// Uses the configured dashboard root user and the built-in login page.
    /// </summary>
    RootUser = 0,

    /// <summary>
    /// Uses the current ASP.NET Core authenticated principal.
    /// </summary>
    AspNetCoreAuthentication = 1,

    /// <summary>
    /// Uses a custom <see cref="ISquirrelBoxDashboardAuthenticator"/> implementation.
    /// </summary>
    Custom = 2
}
