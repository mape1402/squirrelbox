using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SquirrelBox.AspNetCore.Dashboard;

/// <summary>
/// Provides endpoint mapping extensions for the SquirrelBox dashboard.
/// </summary>
public static class SquirrelBoxDashboardEndpointRouteBuilderExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string ProtectorPurpose = "SquirrelBox.AspNetCore.Dashboard.Authentication";

    /// <summary>
    /// Maps the SquirrelBox dashboard endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="pattern">The dashboard route prefix.</param>
    /// <returns>The same endpoint route builder for fluent registration.</returns>
    public static IEndpointRouteBuilder MapSquirrelBoxDashboard(
        this IEndpointRouteBuilder endpoints,
        string pattern = "/squirrelbox")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ValidateDashboardConfiguration(endpoints.ServiceProvider);

        var group = endpoints.MapGroup(string.IsNullOrWhiteSpace(pattern) ? "/squirrelbox" : pattern.TrimEnd('/'));

        Func<HttpContext, Task<IResult>> index = HandleIndexAsync;
        Func<HttpContext, Task<IResult>> login = HandleLoginAsync;
        Func<HttpContext, IResult> logout = HandleLogoutAsync;
        Func<HttpContext, Task<IResult>> state = HandleStateAsync;
        Func<HttpContext, Task> stream = HandleEventStreamAsync;

        group.MapGet("/", index);
        group.MapPost("/auth/login", login);
        group.MapPost("/auth/logout", logout);
        group.MapGet("/api/state", state);
        group.MapGet("/events/stream", stream);

        return endpoints;
    }

    private static async Task<IResult> HandleIndexAsync(HttpContext context)
    {
        var user = await AuthenticateAsync(context);
        if (user is null)
        {
            var options = GetOptions(context);
            return options.Authentication.Mode == SquirrelBoxDashboardAuthenticationMode.AspNetCoreAuthentication
                ? Results.Unauthorized()
                : Results.Content(SquirrelBoxDashboardHtml.Login, "text/html; charset=utf-8");
        }

        return Results.Content(SquirrelBoxDashboardHtml.Dashboard, "text/html; charset=utf-8");
    }

    private static async Task<IResult> HandleLoginAsync(HttpContext context)
    {
        var options = GetOptions(context);
        var login = await ReadLoginAsync(context);

        if (options.Authentication.Mode == SquirrelBoxDashboardAuthenticationMode.AspNetCoreAuthentication)
            return Results.BadRequest(new { error = "Dashboard login is disabled because ASP.NET Core authentication mode is active." });

        SquirrelBoxDashboardUser user = null;

        if (options.Authentication.Mode == SquirrelBoxDashboardAuthenticationMode.RootUser)
        {
            user = AuthenticateRootUser(options, login);
        }
        else
        {
            var authenticator = context.RequestServices.GetRequiredService<ISquirrelBoxDashboardAuthenticator>();
            var result = await authenticator.AuthenticateAsync(
                new SquirrelBoxDashboardAuthRequest
                {
                    HttpContext = context,
                    Username = login.Username,
                    Password = login.Password
                },
                context.RequestAborted);

            if (result.Succeeded)
                user = result.User;
        }

        if (user is null)
            return Results.Unauthorized();

        SignIn(context, user, options);
        return Results.Json(new { authenticated = true, user = user.DisplayName ?? user.Username });
    }

    private static IResult HandleLogoutAsync(HttpContext context)
    {
        var options = GetOptions(context);
        context.Response.Cookies.Delete(options.Authentication.CookieName);
        return Results.Json(new { authenticated = false });
    }

    private static async Task<IResult> HandleStateAsync(HttpContext context)
    {
        if (await AuthenticateAsync(context) is null)
            return Results.Unauthorized();

        var options = GetOptions(context);
        var limit = Math.Max(1, options.RecentLimit);
        var inboxStore = context.RequestServices.GetService<IInboxDiagnosticsStore>();
        var outboxStore = context.RequestServices.GetService<IOutboxStore>();
        var sink = context.RequestServices.GetRequiredService<ISquirrelBoxEventSink>();

        var inbox = inboxStore is null
            ? []
            : (await inboxStore.QueryAsync(new InboxQuery { Limit = limit }, context.RequestAborted))
                .Select(ToInboxItem)
                .ToArray();

        var outbox = outboxStore is null
            ? []
            : (await outboxStore.QueryAsync(new OutboxQuery { Limit = limit }, context.RequestAborted))
                .Select(ToOutboxItem)
                .ToArray();

        return Results.Json(new
        {
            inbox,
            outbox,
            events = sink.GetRecent(limit)
        }, JsonOptions);
    }

    private static async Task HandleEventStreamAsync(HttpContext context)
    {
        if (await AuthenticateAsync(context) is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var sink = context.RequestServices.GetRequiredService<ISquirrelBoxEventSink>();
        var channel = Channel.CreateUnbounded<SquirrelBoxEvent>();

        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.ContentType = "text/event-stream; charset=utf-8";

        using var subscription = sink.Subscribe((@event, cancellationToken) =>
        {
            channel.Writer.TryWrite(@event);
            return ValueTask.CompletedTask;
        });

        try
        {
            await foreach (var @event in channel.Reader.ReadAllAsync(context.RequestAborted))
            {
                await WriteEventAsync(context, @event);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task WriteEventAsync(HttpContext context, SquirrelBoxEvent @event)
    {
        var json = JsonSerializer.Serialize(@event, JsonOptions);
        await context.Response.WriteAsync("event: squirrelbox\n", context.RequestAborted);
        await context.Response.WriteAsync($"data: {json}\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
    }

    private static async Task<SquirrelBoxDashboardUser> AuthenticateAsync(HttpContext context)
    {
        var options = GetOptions(context);
        var cookieUser = TryReadCookie(context, options);
        if (cookieUser is not null)
        {
            SetCurrentUser(context, cookieUser);
            return cookieUser;
        }

        if (options.Authentication.Mode == SquirrelBoxDashboardAuthenticationMode.AspNetCoreAuthentication)
        {
            var principal = context.User;
            if (principal?.Identity?.IsAuthenticated != true)
                return null;

            var user = new SquirrelBoxDashboardUser
            {
                Username = principal.Identity.Name,
                DisplayName = principal.Identity.Name
            };

            foreach (var role in principal.Claims.Where(claim => claim.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase) || claim.Type == "role"))
                user.Roles.Add(role.Value);

            SetCurrentUser(context, user);
            return user;
        }

        if (options.Authentication.Mode == SquirrelBoxDashboardAuthenticationMode.Custom)
        {
            var authenticator = context.RequestServices.GetRequiredService<ISquirrelBoxDashboardAuthenticator>();
            var result = await authenticator.AuthenticateAsync(
                new SquirrelBoxDashboardAuthRequest { HttpContext = context },
                context.RequestAborted);

            if (!result.Succeeded)
                return null;

            SetCurrentUser(context, result.User);
            return result.User;
        }

        return null;
    }

    private static SquirrelBoxDashboardUser AuthenticateRootUser(
        SquirrelBoxDashboardOptions options,
        LoginRequest login)
    {
        var root = options.Authentication.RootUser;
        if (!string.Equals(root.Username, login.Username, StringComparison.Ordinal))
            return null;

        var expectedHash = !string.IsNullOrWhiteSpace(root.PasswordHash)
            ? root.PasswordHash
            : ComputeHash(root.Password);

        if (!string.Equals(expectedHash, ComputeHash(login.Password), StringComparison.OrdinalIgnoreCase))
            return null;

        return new SquirrelBoxDashboardUser
        {
            Username = root.Username,
            DisplayName = root.Username
        };
    }

    private static async Task<LoginRequest> ReadLoginAsync(HttpContext context)
    {
        if (context.Request.HasFormContentType)
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            return new LoginRequest(form["username"], form["password"]);
        }

        return await JsonSerializer.DeserializeAsync<LoginRequest>(
            context.Request.Body,
            JsonOptions,
            context.RequestAborted) ?? new LoginRequest(null, null);
    }

    private static void SignIn(
        HttpContext context,
        SquirrelBoxDashboardUser user,
        SquirrelBoxDashboardOptions options)
    {
        var protector = GetProtector(context);
        var raw = string.Join("|",
            user.Username,
            user.DisplayName ?? user.Username,
            string.Join(",", user.Roles),
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        context.Response.Cookies.Append(
            options.Authentication.CookieName,
            protector.Protect(raw),
            new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Strict,
                Secure = context.Request.IsHttps,
                Path = context.Request.PathBase.HasValue ? context.Request.PathBase.Value : "/"
            });
    }

    private static SquirrelBoxDashboardUser TryReadCookie(
        HttpContext context,
        SquirrelBoxDashboardOptions options)
    {
        if (!context.Request.Cookies.TryGetValue(options.Authentication.CookieName, out var protectedValue))
            return null;

        try
        {
            var raw = GetProtector(context).Unprotect(protectedValue);
            var parts = raw.Split('|');
            if (parts.Length < 2)
                return null;

            var user = new SquirrelBoxDashboardUser
            {
                Username = parts[0],
                DisplayName = parts[1]
            };

            if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
            {
                foreach (var role in parts[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    user.Roles.Add(role);
            }

            return user;
        }
        catch
        {
            return null;
        }
    }

    private static object ToInboxItem(InboxEntry entry)
        => new
        {
            id = entry.Id.ToString(),
            entry.Source,
            entry.Operation,
            entry.IdempotencyKey,
            status = entry.Status.ToString(),
            executionMode = entry.ExecutionMode.ToString(),
            entry.CorrelationId,
            entry.CreatedOnUtc,
            entry.UpdatedOnUtc,
            entry.CompletedOnUtc,
            entry.ExpiresOnUtc,
            failure = entry.Failure
        };

    private static object ToOutboxItem(OutboxEnvelope envelope)
        => new
        {
            id = envelope.Id.ToString(),
            envelope.Transport,
            envelope.Operation,
            envelope.Destination,
            status = envelope.Status.ToString(),
            envelope.CorrelationId,
            envelope.TraceId,
            envelope.PayloadType,
            envelope.ContentType,
            envelope.CreatedOnUtc,
            envelope.UpdatedOnUtc,
            envelope.PublishingOnUtc,
            envelope.PublishedOnUtc,
            envelope.NextAttemptOnUtc,
            envelope.Attempts,
            failure = envelope.Failure?.Details,
            metadata = envelope.Metadata
        };

    private static void ValidateDashboardConfiguration(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptions<SquirrelBoxDashboardOptions>>().Value;
        if (options.Authentication.Mode == SquirrelBoxDashboardAuthenticationMode.RootUser)
        {
            var root = options.Authentication.RootUser;
            if (string.IsNullOrWhiteSpace(root.Username) ||
                string.IsNullOrWhiteSpace(root.Password) && string.IsNullOrWhiteSpace(root.PasswordHash))
            {
                throw new InvalidOperationException(
                    "SquirrelBox dashboard root user is not configured. Configure a root username and password, or choose ASP.NET Core/custom authentication.");
            }
        }

        if (options.Authentication.Mode == SquirrelBoxDashboardAuthenticationMode.Custom &&
            services.GetService<ISquirrelBoxDashboardAuthenticator>() is null)
        {
            throw new InvalidOperationException(
                "SquirrelBox dashboard custom authentication requires an ISquirrelBoxDashboardAuthenticator registration.");
        }
    }

    private static void SetCurrentUser(HttpContext context, SquirrelBoxDashboardUser user)
        => context.Items[SquirrelBoxDashboardUserAccessor.ItemKey] = user;

    private static SquirrelBoxDashboardOptions GetOptions(HttpContext context)
        => context.RequestServices.GetRequiredService<IOptions<SquirrelBoxDashboardOptions>>().Value;

    private static IDataProtector GetProtector(HttpContext context)
        => context.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector(ProtectorPurpose);

    private static string ComputeHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed record LoginRequest(string Username, string Password);
}
