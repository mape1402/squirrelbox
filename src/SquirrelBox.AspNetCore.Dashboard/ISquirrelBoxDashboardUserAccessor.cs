namespace SquirrelBox.AspNetCore.Dashboard;

/// <summary>
/// Provides access to the current dashboard user.
/// </summary>
public interface ISquirrelBoxDashboardUserAccessor
{
    /// <summary>
    /// Gets the current dashboard user.
    /// </summary>
    SquirrelBoxDashboardUser Current { get; }
}
