namespace SquirrelBox.AspNetCore.Dashboard;

/// <summary>
/// Configures the SquirrelBox dashboard.
/// </summary>
public sealed class SquirrelBoxDashboardOptions
{
    /// <summary>
    /// Gets the authentication options.
    /// </summary>
    public SquirrelBoxDashboardAuthenticationOptions Authentication { get; } = new();

    /// <summary>
    /// Gets or sets how many recent records and events are loaded by default.
    /// </summary>
    public int RecentLimit { get; set; } = 100;

    /// <summary>
    /// Gets or sets whether payload values should be hidden from the dashboard.
    /// </summary>
    public bool RedactPayloads { get; set; } = true;
}
