namespace SquirrelBox.AspNetCore.Dashboard;

/// <summary>
/// Configures the built-in dashboard root user.
/// </summary>
public sealed class SquirrelBoxDashboardRootUser
{
    /// <summary>
    /// Gets or sets the root username.
    /// </summary>
    public string Username { get; set; }

    /// <summary>
    /// Gets or sets the root password. Prefer environment configuration in real applications.
    /// </summary>
    public string Password { get; set; }

    /// <summary>
    /// Gets or sets an optional SHA-256 password hash encoded as lowercase hexadecimal.
    /// </summary>
    public string PasswordHash { get; set; }
}
