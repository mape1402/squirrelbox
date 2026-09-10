using Microsoft.AspNetCore.Http;

namespace SquirrelBox.AspNetCore.Dashboard;

/// <summary>
/// HTTP-context-backed implementation of <see cref="ISquirrelBoxDashboardUserAccessor"/>.
/// </summary>
public sealed class SquirrelBoxDashboardUserAccessor : ISquirrelBoxDashboardUserAccessor
{
    internal const string ItemKey = "__SquirrelBoxDashboardUser";
    private readonly IHttpContextAccessor _httpContextAccessor;

    /// <summary>
    /// Initializes a new instance of the <see cref="SquirrelBoxDashboardUserAccessor"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">The HTTP context accessor.</param>
    public SquirrelBoxDashboardUserAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    /// <inheritdoc />
    public SquirrelBoxDashboardUser Current
        => _httpContextAccessor.HttpContext?.Items[ItemKey] as SquirrelBoxDashboardUser;
}
