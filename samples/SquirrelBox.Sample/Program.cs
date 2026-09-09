using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Mule;
using Mule.InMemory;
using Pigeon.Messaging.Consuming.Management;
using Pigeon.Messaging.Contracts;
using Pigeon.Messaging.InMemory;
using Pigeon.Messaging.Producing;
using SquirrelBox;
using SquirrelBox.AspNetCore;
using SquirrelBox.InMemory;
using SquirrelBox.Messaging.Pigeon;
using SquirrelBox.Mule;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Pigeon:Domain"] = "squirrelbox-sample"
});

builder.Services.AddSingleton<SampleOrderStore>();
builder.Services.Configure<MuleSettings>(settings =>
{
    settings.DispatchInterval = TimeSpan.FromMilliseconds(100);
    settings.RetryDelay = TimeSpan.FromMilliseconds(100);
    settings.MaxAttempts = 3;
});

builder.Services
    .AddSquirrelBox(options =>
    {
        options.DefaultEntryLifetime = TimeSpan.FromHours(24);
        options.ScanAssemblyContaining<OrderFingerprintProfile>();
    })
    .UseInMemory();

builder.Services.AddScoped<CreateOrderOperation>();

builder.Services.AddSquirrelBoxAspNetCore(options =>
{
    options.RequestHeaderNames.Clear();
    options.RequestHeaderNames.Add("Idempotency-Key");
    options.ResponseHeaderName = "Idempotency-Key";
});

builder.Services.AddSquirrelBoxMule();
builder.Services.AddMule(mule => mule
    .UseInMemory()
    .AddActionsFromAssemblyContaining<SquirrelBoxOperationMuleAction>()
    .AddActionsFromAssemblyContaining<SquirrelBoxPigeonMuleAction>());

var pigeon = builder.Services.AddPigeon(builder.Configuration, settings =>
{
    settings.ConfigureConsumerExecution(execution =>
    {
        execution.AcknowledgementMode = MessageAcknowledgementMode.OnHandlerSuccess;
    });

    settings.UseInMemoryBroker();
});

builder.Services.AddSquirrelBoxPigeon(options =>
{
    options.ExecutionModeResolver = context =>
        string.Equals(context.Topic, SamplePigeonRoutes.DeferredTopic, StringComparison.OrdinalIgnoreCase)
            ? InboxExecutionMode.Deferred
            : InboxExecutionMode.Inline;
});

pigeon.AddConsumeHandler<OrderMessage>(
    SamplePigeonRoutes.InlineTopic,
    SemanticVersion.Default,
    "inline",
    (context, message) =>
    {
        context.Services.GetRequiredService<SampleOrderStore>()
            .MarkMessage(message, "pigeon-inline");
        return Task.CompletedTask;
    });

pigeon.AddConsumeHandler<OrderMessage>(
    SamplePigeonRoutes.DeferredTopic,
    SemanticVersion.Default,
    "deferred",
    (context, message) =>
    {
        context.Services.GetRequiredService<SampleOrderStore>()
            .MarkMessage(message, "pigeon-deferred");
        return Task.CompletedTask;
    });

var app = builder.Build();

app.UseSquirrelBox();

app.MapGet("/", () => Results.Ok(new
{
    name = "SquirrelBox sample",
    endpoints = new[]
    {
        "POST /orders/inline executes a declared operation inline.",
        "POST /orders/computed-key executes inline and returns a computed Idempotency-Key.",
        "POST /orders/deferred schedules the declared operation through Mule.",
        "POST /pigeon/inline publishes a message consumed inline through Pigeon.",
        "POST /pigeon/deferred publishes a message consumed later through Mule and Pigeon replay.",
        "GET /orders shows order state.",
        "GET /inbox/{entryId} shows inbox state."
    }
}));

app.MapPost("/orders/inline", async (
    CreateOrderRequest request,
    ISquirrelBoxOperationService operations,
    CancellationToken cancellationToken) =>
{
    var result = await operations.ExecuteAsync<CreateOrderOperation, CreateOrderRequest, OrderSnapshot>(
        request,
        cancellationToken);

    return ToHttpResult(result);
});

app.MapPost("/orders/computed-key", async (
    CreateOrderRequest request,
    ISquirrelBoxOperationService operations,
    CancellationToken cancellationToken) =>
{
    var result = await operations.ExecuteAsync<CreateOrderOperation, CreateOrderRequest, OrderSnapshot>(
        request,
        cancellationToken);

    return ToHttpResult(result);
});

app.MapPost("/orders/deferred", async (
    CreateOrderRequest request,
    HttpContext http,
    IInboxService inbox,
    ISquirrelBoxOperationService operations,
    CancellationToken cancellationToken) =>
{
    var open = await http.OpenSquirrelBoxAsync(
        inbox,
        request,
        executionMode: InboxExecutionMode.Deferred,
        cancellationToken: cancellationToken);

    if (!open.Accepted)
        return Results.Conflict(InboxRejectedResponse.From(open));

    var result = await operations.ExecuteAsync<CreateOrderOperation, CreateOrderRequest, OrderSnapshot>(
        request,
        cancellationToken);

    return ToHttpResult(result);
});

app.MapPost("/pigeon/inline", async (
    OrderMessage message,
    IProducer producer,
    CancellationToken cancellationToken) =>
{
    await producer.PublishAsync(message, SamplePigeonRoutes.InlineTopic, SemanticVersion.Default, cancellationToken);
    return Results.Accepted($"/orders/{message.OrderId}", new { message.OrderId, topic = SamplePigeonRoutes.InlineTopic });
});

app.MapPost("/pigeon/deferred", async (
    OrderMessage message,
    IProducer producer,
    CancellationToken cancellationToken) =>
{
    await producer.PublishAsync(message, SamplePigeonRoutes.DeferredTopic, SemanticVersion.Default, cancellationToken);
    return Results.Accepted($"/orders/{message.OrderId}", new { message.OrderId, topic = SamplePigeonRoutes.DeferredTopic });
});

app.MapGet("/orders", (SampleOrderStore orders)
    => Results.Ok(orders.List()));

app.MapGet("/orders/{id}", (string id, SampleOrderStore orders)
    => orders.TryGet(id, out var order)
        ? Results.Ok(order)
        : Results.NotFound());

app.MapGet("/inbox/{entryId}", async (
    string entryId,
    IInboxService inbox,
    CancellationToken cancellationToken) =>
{
    if (!Ulid.TryParse(entryId, out var ulid))
        return Results.BadRequest(new { message = "entryId must be a ULID." });

    var entry = await inbox.GetAsync(ulid, cancellationToken);
    return Results.Ok(new
    {
        id = entry.Id.ToString(),
        entry.Source,
        entry.Operation,
        entry.IdempotencyKey,
        status = entry.Status.ToString(),
        executionMode = entry.ExecutionMode.ToString(),
        entry.PayloadHash,
        entry.CorrelationId,
        entry.CompletedOnUtc,
        entry.Failure,
        entry.Completion?.Metadata
    });
});

app.Run();

static IResult ToHttpResult(SquirrelBoxOperationResult<OrderSnapshot> result)
    => result.State switch
    {
        SquirrelBoxOperationExecutionState.Executed => Results.Created(
            $"/orders/{result.Result.Id}",
            OrderAcceptedResponse.From(result.Result, result.EffectiveIdempotencyKey, result.Context.Entry.Id)),
        SquirrelBoxOperationExecutionState.Deferred => Results.Accepted(
            $"/inbox/{result.Context.Entry.Id}",
            DeferredAcceptedResponse.From(result.EffectiveIdempotencyKey, result.Context.Entry.Id)),
        SquirrelBoxOperationExecutionState.Replayed when result.Result is not null => Results.Ok(
            OrderAcceptedResponse.From(result.Result, result.EffectiveIdempotencyKey, result.Decision.Entry.Id)),
        _ => Results.Conflict(InboxRejectedResponse.From(result.Decision))
    };

/// <summary>
/// Demonstrates semantic fingerprint selection for computed HTTP idempotency keys.
/// </summary>
public sealed class OrderFingerprintProfile : InboxFingerprintProfile
{
    /// <inheritdoc />
    public override void Configure(InboxFingerprintProfileBuilder builder)
        => builder.For<CreateOrderRequest>().Use(request => new
        {
            request.CustomerId,
            request.ExternalOrderId,
            request.Amount
        });
}

/// <summary>
/// Declared SquirrelBox operation used by HTTP endpoints.
/// </summary>
public sealed class CreateOrderOperation : SquirrelBoxOperation<CreateOrderRequest, OrderSnapshot>
{
    /// <inheritdoc />
    protected override ValueTask<OrderSnapshot> ExecuteAsync(
        CreateOrderRequest request,
        SquirrelBoxOperationContext context,
        CancellationToken cancellationToken)
    {
        var order = context.Services.GetRequiredService<SampleOrderStore>()
            .MarkCreated(request);

        return ValueTask.FromResult(order);
    }
}

/// <summary>
/// Pigeon sample routes.
/// </summary>
public static class SamplePigeonRoutes
{
    /// <summary>
    /// Gets the inline sample topic.
    /// </summary>
    public const string InlineTopic = "sample.orders.inline";

    /// <summary>
    /// Gets the deferred sample topic.
    /// </summary>
    public const string DeferredTopic = "sample.orders.deferred";
}

/// <summary>
/// In-memory order store used by the sample application.
/// </summary>
public sealed class SampleOrderStore
{
    private readonly ConcurrentDictionary<string, OrderSnapshot> _orders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Marks an order as created by an HTTP operation.
    /// </summary>
    /// <param name="request">The incoming order request.</param>
    /// <returns>The stored order snapshot.</returns>
    public OrderSnapshot MarkCreated(CreateOrderRequest request)
        => Upsert(request.ExternalOrderId, request.CustomerId, request.Amount, "http-operation");

    /// <summary>
    /// Marks an order from a Pigeon message.
    /// </summary>
    /// <param name="message">The incoming order message.</param>
    /// <param name="status">The status to store.</param>
    /// <returns>The stored order snapshot.</returns>
    public OrderSnapshot MarkMessage(OrderMessage message, string status)
        => Upsert(message.OrderId, message.CustomerId, message.Amount, status);

    /// <summary>
    /// Lists stored orders.
    /// </summary>
    /// <returns>The current order snapshots.</returns>
    public IReadOnlyCollection<OrderSnapshot> List()
        => _orders.Values.OrderBy(order => order.Id, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>
    /// Attempts to get an order by id.
    /// </summary>
    /// <param name="id">The order id.</param>
    /// <param name="order">The stored order snapshot when found.</param>
    /// <returns><see langword="true"/> when the order exists; otherwise, <see langword="false"/>.</returns>
    public bool TryGet(string id, out OrderSnapshot? order)
        => _orders.TryGetValue(id, out order);

    private OrderSnapshot Upsert(string orderId, string customerId, decimal amount, string status)
    {
        var order = new OrderSnapshot(orderId, customerId, amount, status, DateTimeOffset.UtcNow);
        _orders[order.Id] = order;
        return order;
    }
}

/// <summary>
/// Request used by the sample HTTP order endpoints.
/// </summary>
/// <param name="CustomerId">The customer id.</param>
/// <param name="ExternalOrderId">The client-side order id.</param>
/// <param name="Amount">The order amount.</param>
/// <param name="Note">A free-form note ignored by the fingerprint profile.</param>
public sealed record CreateOrderRequest(
    string CustomerId,
    string ExternalOrderId,
    decimal Amount,
    string? Note = null);

/// <summary>
/// Message used by Pigeon sample endpoints.
/// </summary>
/// <param name="OrderId">The order id.</param>
/// <param name="CustomerId">The customer id.</param>
/// <param name="Amount">The order amount.</param>
public sealed record OrderMessage(string OrderId, string CustomerId, decimal Amount);

/// <summary>
/// Stored sample order state.
/// </summary>
/// <param name="Id">The order id.</param>
/// <param name="CustomerId">The customer id.</param>
/// <param name="Amount">The order amount.</param>
/// <param name="Status">The current sample status.</param>
/// <param name="UpdatedOnUtc">The last update timestamp.</param>
public sealed record OrderSnapshot(
    string Id,
    string CustomerId,
    decimal Amount,
    string Status,
    DateTimeOffset UpdatedOnUtc);

/// <summary>
/// Response returned by inline order endpoints.
/// </summary>
/// <param name="Order">The order snapshot.</param>
/// <param name="IdempotencyKey">The explicit or computed idempotency key.</param>
/// <param name="InboxEntryId">The SquirrelBox inbox entry id.</param>
public sealed record OrderAcceptedResponse(
    OrderSnapshot Order,
    string? IdempotencyKey,
    string InboxEntryId)
{
    /// <summary>
    /// Creates an inline order response from an inbox entry.
    /// </summary>
    public static OrderAcceptedResponse From(OrderSnapshot order, string? idempotencyKey, Ulid entryId)
        => new(order, idempotencyKey, entryId.ToString());
}

/// <summary>
/// Response returned when deferred work is accepted.
/// </summary>
/// <param name="IdempotencyKey">The effective idempotency key.</param>
/// <param name="InboxEntryId">The SquirrelBox inbox entry id.</param>
public sealed record DeferredAcceptedResponse(string? IdempotencyKey, string InboxEntryId)
{
    /// <summary>
    /// Creates a deferred response from inbox identifiers.
    /// </summary>
    public static DeferredAcceptedResponse From(string? idempotencyKey, Ulid entryId)
        => new(idempotencyKey, entryId.ToString());
}

/// <summary>
/// Response returned when SquirrelBox rejects duplicate or conflicting work.
/// </summary>
/// <param name="State">The inbox open state.</param>
/// <param name="Action">The policy action.</param>
/// <param name="IdempotencyKey">The effective idempotency key, when known.</param>
/// <param name="InboxEntryId">The inbox entry id, when known.</param>
public sealed record InboxRejectedResponse(
    string State,
    string Action,
    string? IdempotencyKey,
    string? InboxEntryId)
{
    /// <summary>
    /// Creates a rejection response from an inbox open result.
    /// </summary>
    public static InboxRejectedResponse From(InboxOpenResult open)
        => new(open.State.ToString(), "OpenRejected", open.EffectiveIdempotencyKey, open.Entry?.Id.ToString());

    /// <summary>
    /// Creates a rejection response from an inbox decision.
    /// </summary>
    public static InboxRejectedResponse From(InboxDecision decision)
        => new(
            decision.State.ToString(),
            decision.Action.ToString(),
            decision.EffectiveIdempotencyKey,
            decision.Entry?.Id.ToString());
}
