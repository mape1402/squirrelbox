using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.AspNetCore;
using SquirrelBox.InMemory;

namespace SquirrelBox.AspNetCore.Tests;

public sealed class SquirrelBoxPayloadFilterE2ETests
{
    [Fact]
    public async Task Mvc_attribute_opens_from_bound_payload_when_key_header_is_missing()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/payload-orders", new PayloadOrderRequest("computed-mvc"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Idempotency-Key", out var values));
        Assert.False(string.IsNullOrWhiteSpace(values.Single()));
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.GetValues("Correlation-Id").Single()));
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.GetValues("SquirrelBox-Attempt-Id").Single()));
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.GetValues("Trace-Id").Single()));
    }

    [Fact]
    public async Task Mvc_attribute_replays_completed_response_for_duplicate_computed_payload_key()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();
        var request = new PayloadOrderRequest("computed-replay");

        var first = await client.PostAsJsonAsync("/payload-orders", request);
        var duplicate = await client.PostAsJsonAsync("/payload-orders", request);

        var firstBody = await first.Content.ReadAsStringAsync();
        var duplicateBody = await duplicate.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
        Assert.Equal(firstBody, duplicateBody);
        Assert.Equal(
            first.Headers.GetValues("Correlation-Id").Single(),
            duplicate.Headers.GetValues("Correlation-Id").Single());
        Assert.NotEqual(
            first.Headers.GetValues("SquirrelBox-Attempt-Id").Single(),
            duplicate.Headers.GetValues("SquirrelBox-Attempt-Id").Single());
    }

    [Fact]
    public async Task Mvc_attribute_rejects_same_header_key_with_different_payload()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        using var first = new HttpRequestMessage(HttpMethod.Post, "/payload-orders");
        first.Headers.Add("Idempotency-Key", "same-key");
        first.Content = JsonContent.Create(new PayloadOrderRequest("payload-a"));
        var firstResponse = await client.SendAsync(first);

        using var second = new HttpRequestMessage(HttpMethod.Post, "/payload-orders");
        second.Headers.Add("Idempotency-Key", "same-key");
        second.Content = JsonContent.Create(new PayloadOrderRequest("payload-b"));
        var conflict = await client.SendAsync(second);
        var body = await conflict.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        AssertOpenState(body, InboxOpenState.PayloadConflict);
        Assert.Equal("same-key", conflict.Headers.GetValues("Idempotency-Key").Single());
    }

    [Fact]
    public async Task Mvc_attribute_uses_configured_action_argument_name()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/payload-orders/custom", new PayloadOrderRequest("custom-argument"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Idempotency-Key", out var values));
        Assert.False(string.IsNullOrWhiteSpace(values.Single()));
    }

    [Fact]
    public async Task Minimal_api_filter_opens_from_bound_payload_when_key_header_is_missing()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/minimal-orders", new PayloadOrderRequest("computed-minimal"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Idempotency-Key", out var values));
        Assert.False(string.IsNullOrWhiteSpace(values.Single()));
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.GetValues("Correlation-Id").Single()));
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.GetValues("SquirrelBox-Attempt-Id").Single()));
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.GetValues("Trace-Id").Single()));
    }

    [Fact]
    public async Task Minimal_api_filter_uses_incoming_header_name_when_propagating_identity()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/minimal-orders");
        request.Headers.Add("X-Idempotency-Key", "minimal-custom-key");
        request.Content = JsonContent.Create(new PayloadOrderRequest("custom-header"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("minimal-custom-key", response.Headers.GetValues("X-Idempotency-Key").Single());
    }

    [Fact]
    public async Task Minimal_api_filter_exposes_identity_accessor_to_endpoint_scope()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/minimal-identity", new PayloadOrderRequest("identity-scope"));
        var body = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            response.Headers.GetValues("Idempotency-Key").Single(),
            document.RootElement.GetProperty("idempotencyKey").GetString());
    }

    [Fact]
    public async Task Minimal_api_filter_rejects_same_header_key_with_different_payload()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        using var first = new HttpRequestMessage(HttpMethod.Post, "/minimal-orders");
        first.Headers.Add("Idempotency-Key", "minimal-same-key");
        first.Content = JsonContent.Create(new PayloadOrderRequest("minimal-a"));
        var firstResponse = await client.SendAsync(first);

        using var second = new HttpRequestMessage(HttpMethod.Post, "/minimal-orders");
        second.Headers.Add("Idempotency-Key", "minimal-same-key");
        second.Content = JsonContent.Create(new PayloadOrderRequest("minimal-b"));
        var conflict = await client.SendAsync(second);
        var body = await conflict.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        AssertOpenState(body, InboxOpenState.PayloadConflict);
        Assert.Equal("minimal-same-key", conflict.Headers.GetValues("Idempotency-Key").Single());
    }

    private static TestServer CreateServer()
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services
                    .AddControllers()
                    .AddApplicationPart(typeof(PayloadOrdersController).Assembly);
                services.AddSquirrelBox().UseInMemory();
                services.AddSquirrelBoxAspNetCore();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseSquirrelBox();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                    endpoints
                        .MapPost("/minimal-orders", (PayloadOrderRequest request, HttpContext context) =>
                        {
                            context.Response.Headers.ETag = $"\"minimal-{request.Id}\"";
                            return Results.Created($"/minimal-orders/{request.Id}", new { request.Id });
                        })
                        .WithSquirrelBoxPayload();

                    endpoints
                        .MapPost("/minimal-identity", (
                            PayloadOrderRequest request,
                            ISquirrelBoxIdentityAccessor identityAccessor) =>
                            Results.Ok(new
                            {
                                request.Id,
                                IdempotencyKey = identityAccessor.Current?.Operation.IdempotencyKey.Value
                            }))
                        .WithSquirrelBoxPayload();
                });
            });

        return new TestServer(builder);
    }

    private static void AssertOpenState(string body, InboxOpenState expected)
    {
        using var document = JsonDocument.Parse(body);
        Assert.Equal((int)expected, document.RootElement.GetProperty("State").GetInt32());
    }
}

public sealed record PayloadOrderRequest(string Id);

/// <summary>
/// Test controller used to validate MVC payload filters through real endpoint routing.
/// </summary>
[ApiController]
[Route("payload-orders")]
public sealed class PayloadOrdersController : ControllerBase
{
    [HttpPost]
    [SquirrelBoxPayload]
    public IActionResult Create(PayloadOrderRequest request)
    {
        Response.Headers.ETag = $"\"mvc-{request.Id}\"";
        return Created($"/payload-orders/{request.Id}", new { request.Id });
    }

    [HttpPost("custom")]
    [SquirrelBoxPayload("payload")]
    public IActionResult Custom(PayloadOrderRequest payload)
    {
        Response.Headers.ETag = $"\"mvc-custom-{payload.Id}\"";
        return Created($"/payload-orders/{payload.Id}", new { payload.Id });
    }
}
