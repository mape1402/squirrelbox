using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.AspNetCore;
using SquirrelBox.InMemory;

namespace SquirrelBox.AspNetCore.Tests;

public sealed class SquirrelBoxMiddlewareE2ETests
{
    [Fact]
    public async Task Middleware_replays_completed_response_for_duplicate_explicit_header()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        using var first = new HttpRequestMessage(HttpMethod.Post, "/orders");
        first.Headers.Add("Idempotency-Key", "order-1");
        first.Content = JsonContent.Create(new OrderRequest("order-1"));

        var firstResponse = await client.SendAsync(first);

        using var duplicate = new HttpRequestMessage(HttpMethod.Post, "/orders");
        duplicate.Headers.Add("Idempotency-Key", "order-1");
        duplicate.Content = JsonContent.Create(new OrderRequest("order-1"));

        var duplicateResponse = await client.SendAsync(duplicate);

        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        var duplicateBody = await duplicateResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal("order-1", firstResponse.Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal("\"order-1\"", firstResponse.Headers.ETag?.Tag);
        Assert.Equal(HttpStatusCode.Created, duplicateResponse.StatusCode);
        Assert.Equal("order-1", duplicateResponse.Headers.GetValues("Idempotency-Key").Single());
        Assert.Equal("\"order-1\"", duplicateResponse.Headers.ETag?.Tag);
        Assert.Equal(firstBody, duplicateBody);
    }

    [Fact]
    public async Task Middleware_returns_computed_key_when_endpoint_opens_from_bound_payload()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/orders", new OrderRequest("order-computed"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Idempotency-Key", out var values));
        Assert.False(string.IsNullOrWhiteSpace(values.Single()));
    }

    private static TestServer CreateServer()
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSquirrelBox().UseInMemory();
                services.AddSquirrelBoxAspNetCore();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseSquirrelBox();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapPost("/orders", async context =>
                    {
                        var request = await context.Request.ReadFromJsonAsync<OrderRequest>();
                        var inbox = context.RequestServices.GetRequiredService<IInboxService>();
                        var result = await context.OpenSquirrelBoxAsync(inbox, request);

                        if (result.Accepted)
                        {
                            var verification = await inbox.VerifyCurrentPayloadAsync(request);
                            if (!verification.Success)
                            {
                                context.Response.StatusCode = StatusCodes.Status409Conflict;
                                return;
                            }
                        }

                        context.Response.StatusCode = StatusCodes.Status201Created;
                        context.Response.ContentType = "application/json";
                        context.Response.Headers.ETag = $"\"{request.Id}\"";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(new { id = request.Id }));
                    });
                });
            });

        return new TestServer(builder);
    }

    private sealed record OrderRequest(string Id);
}
