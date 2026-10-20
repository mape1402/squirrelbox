using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SquirrelBox.InMemory;

namespace SquirrelBox.AspNetCore.Tests;

public sealed class SquirrelBoxHttpCorrelationResolutionTests
{
    [Fact]
    public async Task Related_http_entrypoints_generate_then_resolve_the_same_correlation_header()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var first = await client.PostAsJsonAsync("/legacy/a", new LegacyHttpRequest("123"));
        var second = await client.PostAsJsonAsync("/legacy/b", new LegacyHttpRequest("123"));
        var firstCorrelation = first.Headers.GetValues("Correlation-Id").Single();
        var secondCorrelation = second.Headers.GetValues("Correlation-Id").Single();

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(firstCorrelation, secondCorrelation);
        Assert.NotEqual(
            first.Headers.GetValues("Trace-Id").Single(),
            second.Headers.GetValues("Trace-Id").Single());
        Assert.NotEqual(
            first.Headers.GetValues("SquirrelBox-Attempt-Id").Single(),
            second.Headers.GetValues("SquirrelBox-Attempt-Id").Single());
    }

    [Fact]
    public async Task Required_http_resolution_rejects_an_unknown_relationship()
    {
        using var server = CreateServer();
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync("/legacy/b", new LegacyHttpRequest("missing"));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains(nameof(InboxOpenState.MissingCorrelationId), body, StringComparison.Ordinal);
        Assert.False(response.Headers.Contains("Correlation-Id"));
    }

    [Fact]
    public void Mvc_attribute_exposes_correlation_resolution_mode()
    {
        ISquirrelBoxInboxMetadata metadata = new SquirrelBoxInboxAttribute
        {
            CorrelationIdResolutionMode = CorrelationIdResolutionMode.ResolveThenGenerate
        };

        Assert.Equal(CorrelationIdResolutionMode.ResolveThenGenerate, metadata.CorrelationIdResolutionMode);
    }

    private static TestServer CreateServer()
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddSingleton<LegacyHttpCorrelationDatabase>();
                services.AddScoped<ICorrelationIdResolutionStrategy, LegacyHttpCorrelationStrategy>();
                services.AddSquirrelBox().UseInMemory();
                services.AddSquirrelBoxAspNetCore();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseSquirrelBox();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapPost("/legacy/a", (
                            LegacyHttpRequest request,
                            IInboxService inbox,
                            LegacyHttpCorrelationDatabase database) =>
                        {
                            database.Store(request.LegacyId, inbox.Current.Entry.CorrelationId);
                            return Results.Ok(new { request.LegacyId });
                        })
                        .WithSquirrelBoxInbox();

                    endpoints.MapPost("/legacy/b", (LegacyHttpRequest request) =>
                            Results.Ok(new { request.LegacyId }))
                        .WithSquirrelBoxInbox(options =>
                            options.CorrelationIdResolutionMode = CorrelationIdResolutionMode.ResolveRequired);
                });
            });

        return new TestServer(builder);
    }

    private sealed record LegacyHttpRequest(string LegacyId);

    private sealed class LegacyHttpCorrelationDatabase
    {
        private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.Ordinal);

        public void Store(string legacyId, string correlationId)
            => _values[legacyId] = correlationId;

        public bool TryGet(string legacyId, out string correlationId)
            => _values.TryGetValue(legacyId, out correlationId);
    }

    private sealed class LegacyHttpCorrelationStrategy(
        LegacyHttpCorrelationDatabase database) : ICorrelationIdResolutionStrategy
    {
        public string Name => "legacy-http-database";

        public int Order => 0;

        public ValueTask<CorrelationIdResolutionResult> TryResolveAsync(
            InboxIdentityFactoryContext context,
            CancellationToken cancellationToken = default)
        {
            if (context.Payload is not LegacyHttpRequest request)
                return ValueTask.FromResult(CorrelationIdResolutionResult.NotApplicable);

            return ValueTask.FromResult(
                database.TryGet(request.LegacyId, out var correlationId)
                    ? CorrelationIdResolutionResult.Resolved(correlationId)
                    : CorrelationIdResolutionResult.NotFound);
        }
    }
}
