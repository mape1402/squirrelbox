namespace SquirrelBox.AspNetCore.Dashboard;

/// <summary>
/// Configures SquirrelBox dashboard authentication.
/// </summary>
public sealed class SquirrelBoxDashboardAuthenticationOptions
{
    /// <summary>
    /// Gets or sets the dashboard authentication mode.
    /// </summary>
    public SquirrelBoxDashboardAuthenticationMode Mode { get; set; } = SquirrelBoxDashboardAuthenticationMode.RootUser;

    /// <summary>
    /// Gets or sets the built-in root user configuration.
    /// </summary>
    public SquirrelBoxDashboardRootUser RootUser { get; set; } = new();

    /// <summary>
    /// Gets or sets the cookie name used by built-in dashboard authentication.
    /// </summary>
    public string CookieName { get; set; } = "SquirrelBox.Dashboard";
}
