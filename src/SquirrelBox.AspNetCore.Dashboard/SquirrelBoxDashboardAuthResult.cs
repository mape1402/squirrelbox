namespace SquirrelBox.AspNetCore.Dashboard;

/// <summary>
/// Represents the result of dashboard authentication.
/// </summary>
public sealed class SquirrelBoxDashboardAuthResult
{
    /// <summary>
    /// Gets an unauthenticated result.
    /// </summary>
    public static SquirrelBoxDashboardAuthResult Rejected { get; } = new(false);

    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxDashboardAuthResult"/> class.
    /// </summary>
    /// <param name="succeeded">Whether authentication succeeded.</param>
    /// <param name="user">The authenticated user.</param>
    public SquirrelBoxDashboardAuthResult(bool succeeded, SquirrelBoxDashboardUser user = null)
    {
        Succeeded = succeeded;
        User = user;
    }

    /// <summary>
    /// Gets a value indicating whether authentication succeeded.
    /// </summary>
    public bool Succeeded { get; }

    /// <summary>
    /// Gets the authenticated user.
    /// </summary>
    public SquirrelBoxDashboardUser User { get; }

    /// <summary>
    /// Creates an authenticated result.
    /// </summary>
    /// <param name="user">The authenticated dashboard user.</param>
    /// <returns>The authenticated result.</returns>
    public static SquirrelBoxDashboardAuthResult Success(SquirrelBoxDashboardUser user)
        => new(true, user ?? throw new ArgumentNullException(nameof(user)));
}
