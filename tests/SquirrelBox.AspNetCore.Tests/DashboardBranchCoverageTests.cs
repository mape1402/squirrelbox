using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SquirrelBox.AspNetCore.Dashboard;
using SquirrelBox.InMemory;

namespace SquirrelBox.AspNetCore.Tests;

public sealed class DashboardBranchCoverageTests
{
    private static readonly Type EndpointsType = typeof(SquirrelBoxDashboardEndpointRouteBuilderExtensions);

    [Fact]
    public void Dashboard_public_auth_components_reject_null_dependencies()
    {
        Assert.Throws<ArgumentNullException>(() => SquirrelBoxDashboardAuthResult.Success(null));
        Assert.Throws<ArgumentNullException>(() => new SquirrelBoxDashboardUserAccessor(null));
    }

    [Fact]
    public async Task Dashboard_blank_route_pattern_uses_default_path()
    {
        using var server = CreateServer(pattern: " ");
        using var client = server.CreateClient();

        var response = await client.GetAsync("/squirrelbox");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_login_uses_username_when_display_name_is_missing_and_honors_path_base()
    {
        using var server = CreateServer(
            customAuthenticator: new MissingDisplayNameAuthenticator(),
            usePathBase: true);
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/base/squirrelbox/auth/login", new
        {
            username = "operator",
            password = "secret"
        });

        response.EnsureSuccessStatusCode();
        Assert.Contains("operator", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("path=/base", response.Headers.GetValues("Set-Cookie").Single(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Dashboard_json_null_login_uses_empty_credentials()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("null"));
        var method = EndpointsType.GetMethod("ReadLoginAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

        var task = (Task)method.Invoke(null, [context])!;
        await task;
        var login = task.GetType().GetProperty("Result")!.GetValue(task)!;

        Assert.Null(login.GetType().GetProperty("Username")!.GetValue(login));
        Assert.Null(login.GetType().GetProperty("Password")!.GetValue(login));
    }

    [Theory]
    [InlineData("single-part", null)]
    [InlineData("user|display", "user")]
    public void Dashboard_cookie_reader_handles_short_valid_payloads(string raw, string expectedUsername)
    {
        using var provider = CreateDashboardServices();
        var options = provider.GetRequiredService<IOptions<SquirrelBoxDashboardOptions>>().Value;
        var protector = provider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("SquirrelBox.AspNetCore.Dashboard.Authentication");
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Headers.Cookie = $"{options.Authentication.CookieName}={protector.Protect(raw)}";
        var method = EndpointsType.GetMethod("TryReadCookie", BindingFlags.NonPublic | BindingFlags.Static)!;

        var user = method.Invoke(null, [context, options]);

        if (expectedUsername is null)
            Assert.Null(user);
        else
            Assert.Equal(expectedUsername, user!.GetType().GetProperty("Username")!.GetValue(user));
    }

    [Fact]
    public void Dashboard_item_projection_includes_failure_details()
    {
        var failure = new OutboxFailure { Details = "failed" };
        var envelope = new OutboxEnvelope { Id = Ulid.NewUlid(), Failure = failure };
        var attempt = new OutboxAttempt
        {
            Id = Ulid.NewUlid(),
            OutboxEnvelopeId = envelope.Id,
            Failure = failure
        };
        var outboxMethod = EndpointsType.GetMethod("ToOutboxItem", BindingFlags.NonPublic | BindingFlags.Static)!;
        var attemptMethod = EndpointsType.GetMethod("ToOutboxAttemptItem", BindingFlags.NonPublic | BindingFlags.Static)!;

        var outboxItem = outboxMethod.Invoke(null, [envelope])!;
        var attemptItem = attemptMethod.Invoke(null, [attempt])!;

        Assert.Equal("failed", outboxItem.GetType().GetProperty("failure")!.GetValue(outboxItem));
        Assert.Equal("failed", attemptItem.GetType().GetProperty("failure")!.GetValue(attemptItem));
    }

    [Fact]
    public void Dashboard_hash_accepts_null_value()
    {
        var method = EndpointsType.GetMethod("ComputeHash", BindingFlags.NonPublic | BindingFlags.Static)!;

        var hash = Assert.IsType<string>(method.Invoke(null, [null]));

        Assert.Equal(64, hash.Length);
    }

    [Fact]
    public void Dashboard_root_validation_checks_password_after_valid_username()
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSquirrelBox().UseInMemory();
                services.AddSquirrelBoxDashboard(options =>
                {
                    options.Authentication.RootUser.Username = "admin";
                    options.Authentication.RootUser.Password = null;
                    options.Authentication.RootUser.PasswordHash = null;
                });
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapSquirrelBoxDashboard());
            });

        Assert.Throws<InvalidOperationException>(() => new TestServer(builder));
    }

    [Fact]
    public async Task Dashboard_aspnet_authentication_rejects_principal_without_identity()
    {
        using var provider = CreateDashboardServices();
        provider.GetRequiredService<IOptions<SquirrelBoxDashboardOptions>>()
            .Value.Authentication.Mode = SquirrelBoxDashboardAuthenticationMode.AspNetCoreAuthentication;
        var context = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal()
        };
        var method = EndpointsType.GetMethod("AuthenticateAsync", BindingFlags.NonPublic | BindingFlags.Static)!;

        var task = (Task)method.Invoke(null, new object[] { context })!;
        await task;

        Assert.Null(task.GetType().GetProperty("Result")!.GetValue(task));
    }

    private static TestServer CreateServer(
        string pattern = "/squirrelbox",
        ISquirrelBoxDashboardAuthenticator customAuthenticator = null,
        bool usePathBase = false)
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSquirrelBox().UseInMemory();
                if (customAuthenticator is not null)
                    services.AddSingleton(customAuthenticator);
                services.AddSquirrelBoxDashboard(options =>
                {
                    if (customAuthenticator is null)
                    {
                        options.Authentication.RootUser.Username = "admin";
                        options.Authentication.RootUser.Password = "secret";
                    }
                    else
                    {
                        options.Authentication.Mode = SquirrelBoxDashboardAuthenticationMode.Custom;
                    }
                });
            })
            .Configure(app =>
            {
                if (usePathBase)
                    app.UsePathBase("/base");
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapSquirrelBoxDashboard(pattern));
            });

        return new TestServer(builder);
    }

    private static ServiceProvider CreateDashboardServices()
    {
        var services = new ServiceCollection();
        services.AddDataProtection();
        services.AddSingleton<IOptions<SquirrelBoxDashboardOptions>>(Options.Create(new SquirrelBoxDashboardOptions()));
        return services.BuildServiceProvider();
    }

    private sealed class MissingDisplayNameAuthenticator : ISquirrelBoxDashboardAuthenticator
    {
        public Task<SquirrelBoxDashboardAuthResult> AuthenticateAsync(
            SquirrelBoxDashboardAuthRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(SquirrelBoxDashboardAuthResult.Success(new SquirrelBoxDashboardUser
            {
                Username = request.Username,
                DisplayName = null
            }));
    }
}
