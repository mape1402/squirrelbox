using System.Net;
using System.Net.Http.Json;
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
}
