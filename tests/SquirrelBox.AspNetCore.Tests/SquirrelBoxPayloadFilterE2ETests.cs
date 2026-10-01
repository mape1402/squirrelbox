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
    public async Task Minimal_api_filter_reopens_completed_entry_after_configured_window()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 9, 1, 0, 0, TimeSpan.Zero));
        using var server = CreateServer(clock);
        using var client = server.CreateClient();
        var request = new PayloadOrderRequest("window-order");

        var first = await client.PostAsJsonAsync("/minimal-window-orders", request);
        clock.Advance(TimeSpan.FromSeconds(6));
        var afterWindow = await client.PostAsJsonAsync("/minimal-window-orders", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, afterWindow.StatusCode);
        Assert.NotEqual(
            await first.Content.ReadAsStringAsync(),
            await afterWindow.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Minimal_api_filter_keeps_completed_entry_locked_forever_when_configured()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 9, 1, 0, 0, TimeSpan.Zero));
        using var server = CreateServer(clock);
        using var client = server.CreateClient();
        var request = new PayloadOrderRequest("forever-order");

        var first = await client.PostAsJsonAsync("/minimal-forever-orders", request);
        clock.Advance(TimeSpan.FromSeconds(6));
        var duplicate = await client.PostAsJsonAsync("/minimal-forever-orders", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
        Assert.Equal(
            await first.Content.ReadAsStringAsync(),
            await duplicate.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Mvc_attribute_reopens_completed_entry_after_configured_window()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 9, 1, 0, 0, TimeSpan.Zero));
        using var server = CreateServer(clock);
        using var client = server.CreateClient();
        var request = new PayloadOrderRequest("mvc-window-order");

        var first = await client.PostAsJsonAsync("/payload-orders/window", request);
        clock.Advance(TimeSpan.FromSeconds(6));
        var afterWindow = await client.PostAsJsonAsync("/payload-orders/window", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, afterWindow.StatusCode);
        Assert.NotEqual(
            await first.Content.ReadAsStringAsync(),
            await afterWindow.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Mvc_attribute_keeps_completed_entry_locked_forever_when_configured()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 9, 1, 0, 0, TimeSpan.Zero));
        using var server = CreateServer(clock);
        using var client = server.CreateClient();
        var request = new PayloadOrderRequest("mvc-forever-order");

        var first = await client.PostAsJsonAsync("/payload-orders/forever", request);
        clock.Advance(TimeSpan.FromSeconds(6));
        var duplicate = await client.PostAsJsonAsync("/payload-orders/forever", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
        Assert.Equal(
            await first.Content.ReadAsStringAsync(),
            await duplicate.Content.ReadAsStringAsync());
    }

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
    public async Task Mvc_attribute_applies_deferred_entrypoint_settings()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/payload-orders/deferred", new PayloadOrderRequest("mvc-deferred"));
        var body = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(InboxExecutionMode.Deferred.ToString(), document.RootElement.GetProperty("executionMode").GetString());
        Assert.Equal("mvc-deferred", document.RootElement.GetProperty("lane").GetString());
        Assert.Equal(5, document.RootElement.GetProperty("maxAttempts").GetInt32());
        Assert.Equal(1000, document.RootElement.GetProperty("delayMs").GetInt32());
        Assert.Equal(InboxRetryBackoff.Linear.ToString(), document.RootElement.GetProperty("backoff").GetString());
        Assert.Equal(30000, document.RootElement.GetProperty("inProgressTimeoutMs").GetInt32());
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
    public async Task Minimal_api_filter_applies_deferred_entrypoint_settings()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/minimal-deferred-orders", new PayloadOrderRequest("minimal-deferred"));
        var body = await response.Content.ReadAsStringAsync();

        using var document = JsonDocument.Parse(body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(InboxExecutionMode.Deferred.ToString(), document.RootElement.GetProperty("executionMode").GetString());
        Assert.Equal("minimal-deferred", document.RootElement.GetProperty("lane").GetString());
        Assert.Equal(4, document.RootElement.GetProperty("maxAttempts").GetInt32());
        Assert.Equal(250, document.RootElement.GetProperty("delayMs").GetInt32());
        Assert.Equal(InboxRetryBackoff.Exponential.ToString(), document.RootElement.GetProperty("backoff").GetString());
        Assert.Equal(45000, document.RootElement.GetProperty("inProgressTimeoutMs").GetInt32());
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

    private static TestServer CreateServer(TimeProvider timeProvider = null)
    {
        var windowExecutions = 0;
        var foreverExecutions = 0;
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                if (timeProvider is not null)
                    services.AddSingleton(timeProvider);

                services.AddRouting();
                services.AddSingleton<PayloadExecutionCounter>();
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

                    endpoints
                        .MapPost("/minimal-deferred-orders", (PayloadOrderRequest request, IInboxService inbox) =>
                        {
                            var entry = inbox.Current.Entry;
                            inbox.ReleaseCurrent();
                            return Results.Ok(new
                            {
                                request.Id,
                                ExecutionMode = entry.ExecutionMode.ToString(),
                                entry.Deferred.Lane,
                                entry.Deferred.MaxAttempts,
                                DelayMs = Convert.ToInt32(entry.Deferred.Delay?.TotalMilliseconds),
                                Backoff = entry.Deferred.Backoff?.ToString(),
                                InProgressTimeoutMs = Convert.ToInt32(entry.Deferred.InProgressTimeout?.TotalMilliseconds)
                            });
                        })
                        .WithSquirrelBoxPayload(options =>
                        {
                            options.ExecutionMode = InboxExecutionMode.Deferred;
                            options.Deferred.Lane = "minimal-deferred";
                            options.Deferred.MaxAttempts = 4;
                            options.Deferred.Delay = TimeSpan.FromMilliseconds(250);
                            options.Deferred.Backoff = InboxRetryBackoff.Exponential;
                            options.Deferred.InProgressTimeout = TimeSpan.FromSeconds(45);
                        });

                    endpoints
                        .MapPost("/minimal-window-orders", (PayloadOrderRequest request) =>
                            Results.Created(
                                $"/minimal-window-orders/{request.Id}",
                                new
                                {
                                    request.Id,
                                    Execution = Interlocked.Increment(ref windowExecutions)
                                }))
                        .WithSquirrelBoxPayload(options => options.EntryLifetime = TimeSpan.FromSeconds(5));

                    endpoints
                        .MapPost("/minimal-forever-orders", (PayloadOrderRequest request) =>
                            Results.Created(
                                $"/minimal-forever-orders/{request.Id}",
                                new
                                {
                                    request.Id,
                                    Execution = Interlocked.Increment(ref foreverExecutions)
                                }))
                        .WithSquirrelBoxPayload(options =>
                        {
                            options.EntryLifetime = TimeSpan.FromSeconds(5);
                            options.CompletedLock = InboxCompletedLockMode.Forever;
                        });
                });
            });

        return new TestServer(builder);
    }

    private static void AssertOpenState(string body, InboxOpenState expected)
    {
        using var document = JsonDocument.Parse(body);
        Assert.Equal(expected.ToString(), document.RootElement.GetProperty("State").GetString());
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value) => _utcNow = _utcNow.Add(value);
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
    private readonly PayloadExecutionCounter _counter;
    private readonly IInboxService _inbox;

    public PayloadOrdersController(PayloadExecutionCounter counter, IInboxService inbox)
    {
        _counter = counter;
        _inbox = inbox;
    }

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

    [HttpPost("window")]
    [SquirrelBoxPayload(TtlSeconds = 5)]
    public IActionResult Window(PayloadOrderRequest request)
        => Created(
            $"/payload-orders/{request.Id}",
            new
            {
                request.Id,
                Execution = _counter.NextWindow()
            });

    [HttpPost("forever")]
    [SquirrelBoxPayload(TtlSeconds = 5, CompletedLock = InboxCompletedLockMode.Forever)]
    public IActionResult Forever(PayloadOrderRequest request)
        => Created(
            $"/payload-orders/{request.Id}",
            new
            {
                request.Id,
                Execution = _counter.NextForever()
            });

    [HttpPost("deferred")]
    [SquirrelBoxPayload(
        DeferExecution = true,
        DeferredLane = "mvc-deferred",
        RetryMaxAttempts = 5,
        RetryDelaySeconds = 1,
        RetryBackoff = InboxRetryBackoff.Linear,
        InProgressTimeoutSeconds = 30)]
    public IActionResult Deferred(PayloadOrderRequest request)
    {
        var entry = _inbox.Current.Entry;
        _inbox.ReleaseCurrent();
        return Ok(new
        {
            request.Id,
            ExecutionMode = entry.ExecutionMode.ToString(),
            entry.Deferred.Lane,
            entry.Deferred.MaxAttempts,
            DelayMs = Convert.ToInt32(entry.Deferred.Delay?.TotalMilliseconds),
            Backoff = entry.Deferred.Backoff?.ToString(),
            InProgressTimeoutMs = Convert.ToInt32(entry.Deferred.InProgressTimeout?.TotalMilliseconds)
        });
    }
}

public sealed class PayloadExecutionCounter
{
    private int _window;
    private int _forever;

    public int NextWindow() => Interlocked.Increment(ref _window);

    public int NextForever() => Interlocked.Increment(ref _forever);
}
