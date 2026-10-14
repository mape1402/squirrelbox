using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.AspNetCore.Dashboard;
using SquirrelBox.InMemory;

namespace SquirrelBox.AspNetCore.Tests;

public sealed class SquirrelBoxDashboardTests
{
    [Fact]
    public async Task Dashboard_requires_login_before_returning_state()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var response = await client.GetAsync("/squirrelbox/api/state");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_root_login_allows_state_snapshot()
    {
        using var server = CreateServer();
        await SeedAsync(server.Services);

        using var client = server.CreateClient();
        var login = await client.PostAsJsonAsync("/squirrelbox/auth/login", new
        {
            username = "admin",
            password = "secret"
        });

        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("Cookie", login.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);

        var state = await client.GetFromJsonAsync<DashboardState>("/squirrelbox/api/state");

        Assert.NotNull(state);
        Assert.Single(state.Inbox);
        Assert.Single(state.Outbox);
        Assert.NotEmpty(state.Events);
    }

    [Fact]
    public async Task Dashboard_scripts_use_mounted_path_for_client_requests()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var loginPage = await client.GetStringAsync("/squirrelbox");

        Assert.Contains("${dashboardPath}/auth/login", loginPage);
        Assert.DoesNotContain("fetch('auth/login'", loginPage);

        var login = await client.PostAsJsonAsync("/squirrelbox/auth/login", new
        {
            username = "admin",
            password = "secret"
        });

        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("Cookie", login.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);

        var dashboardPage = await client.GetStringAsync("/squirrelbox");

        Assert.Contains("${dashboardPath}/api/state", dashboardPage);
        Assert.Contains("${dashboardPath}/events/stream", dashboardPage);
        Assert.DoesNotContain("fetch('api/state'", dashboardPage);
        Assert.DoesNotContain("new EventSource('events/stream'", dashboardPage);
    }

    [Fact]
    public async Task Dashboard_rejects_invalid_root_login()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var login = await client.PostAsJsonAsync("/squirrelbox/auth/login", new
        {
            username = "admin",
            password = "wrong"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Dashboard_accepts_form_login_and_logout_clears_cookie()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "admin",
            ["password"] = "secret"
        });

        var login = await client.PostAsync("/squirrelbox/auth/login", form);

        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("Cookie", login.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);

        var logout = await client.PostAsync("/squirrelbox/auth/logout", null);

        logout.EnsureSuccessStatusCode();
        Assert.Contains("authenticated", await logout.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Dashboard_event_stream_requires_authenticated_user()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var response = await client.GetAsync("/squirrelbox/events/stream");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_uses_aspnet_core_authenticated_principal()
    {
        using var server = CreateAspNetCoreAuthServer();
        using var client = server.CreateClient();

        var page = await client.GetStringAsync("/squirrelbox");
        var login = await client.PostAsJsonAsync("/squirrelbox/auth/login", new
        {
            username = "ignored",
            password = "ignored"
        });
        var state = await client.GetFromJsonAsync<DashboardState>("/squirrelbox/api/state");

        Assert.Contains("SquirrelBox", page);
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        Assert.NotNull(state);
    }

    [Fact]
    public async Task Dashboard_uses_custom_authenticator_for_login_and_page_access()
    {
        using var server = CreateCustomAuthServer();
        using var client = server.CreateClient();

        var pageBeforeLogin = await client.GetStringAsync("/squirrelbox");
        var login = await client.PostAsJsonAsync("/squirrelbox/auth/login", new
        {
            username = "operator",
            password = "secret"
        });

        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("Cookie", login.Headers.GetValues("Set-Cookie").Single().Split(';')[0]);
        var pageAfterLogin = await client.GetStringAsync("/squirrelbox");

        Assert.Contains("SquirrelBox", pageBeforeLogin);
        Assert.Contains("SquirrelBox", pageAfterLogin);
    }

    [Fact]
    public void Dashboard_requires_custom_authenticator_when_custom_mode_is_selected()
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSquirrelBox().UseInMemory();
                services.AddSquirrelBoxDashboard(options =>
                    options.Authentication.Mode = SquirrelBoxDashboardAuthenticationMode.Custom);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapSquirrelBoxDashboard());
            });

        Assert.Throws<InvalidOperationException>(() => new TestServer(builder));
    }

    [Fact]
    public void Dashboard_requires_root_user_configuration_by_default()
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSquirrelBox().UseInMemory();
                services.AddSquirrelBoxDashboard();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapSquirrelBoxDashboard());
            });

        Assert.Throws<InvalidOperationException>(() => new TestServer(builder));
    }

    private static TestServer CreateServer()
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSquirrelBox().UseInMemory();
                services.AddSquirrelBoxDashboard(options =>
                {
                    options.Authentication.RootUser.Username = "admin";
                    options.Authentication.RootUser.Password = "secret";
                });
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapSquirrelBoxDashboard());
            });

        return new TestServer(builder);
    }

    private static TestServer CreateAspNetCoreAuthServer()
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSquirrelBox();
                services.AddSquirrelBoxDashboard(options =>
                    options.Authentication.Mode = SquirrelBoxDashboardAuthenticationMode.AspNetCoreAuthentication);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.Use(async (context, next) =>
                {
                    context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.Name, "admin"),
                        new Claim(ClaimTypes.Role, "operators")
                    ], "test"));
                    await next();
                });
                app.UseEndpoints(endpoints => endpoints.MapSquirrelBoxDashboard());
            });

        return new TestServer(builder);
    }

    private static TestServer CreateCustomAuthServer()
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<ISquirrelBoxDashboardAuthenticator, TestDashboardAuthenticator>();
                services.AddSquirrelBox().UseInMemory();
                services.AddSquirrelBoxDashboard(options =>
                    options.Authentication.Mode = SquirrelBoxDashboardAuthenticationMode.Custom);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapSquirrelBoxDashboard());
            });

        return new TestServer(builder);
    }

    private static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxService>();
        await inbox.OpenOrContinueAsync(InboxOpenRequest.For(
            source: "http",
            operation: "POST /orders",
            idempotencyKey: "order-1",
            payload: new OrderRequest("order-1"),
            correlationId: "trace-1"));
        await inbox.CompleteCurrentAsync();

        await scope.ServiceProvider.GetRequiredService<IOutboxService>()
            .EnqueueAsync(new OutboxEnqueueRequest
            {
                Transport = "pigeon",
                Operation = "orders.created",
                Destination = "orders",
                Payload = new OrderRequest("order-1"),
                CorrelationId = "trace-1"
            });
    }

    private sealed record OrderRequest(string Id);

    private sealed class DashboardState
    {
        public DashboardInboxItem[] Inbox { get; set; }

        public DashboardOutboxItem[] Outbox { get; set; }

        public SquirrelBoxEvent[] Events { get; set; }
    }

    private sealed class DashboardInboxItem
    {
        public string Id { get; set; }
    }

    private sealed class DashboardOutboxItem
    {
        public string Id { get; set; }
    }

    private sealed class TestDashboardAuthenticator : ISquirrelBoxDashboardAuthenticator
    {
        public Task<SquirrelBoxDashboardAuthResult> AuthenticateAsync(
            SquirrelBoxDashboardAuthRequest request,
            CancellationToken cancellationToken = default)
        {
            var user = new SquirrelBoxDashboardUser
            {
                Username = request.Username ?? "operator",
                DisplayName = "Operator"
            };
            user.Roles.Add("operators");

            return Task.FromResult(SquirrelBoxDashboardAuthResult.Success(user));
        }
    }
}
