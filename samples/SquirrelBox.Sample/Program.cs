using System.Collections.Concurrent;
using Mule;
using Mule.InMemory;
using SquirrelBox;
using SquirrelBox.AspNetCore;
using SquirrelBox.InMemory;
using SquirrelBox.Mule;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddSquirrelBoxAspNetCore(options =>
{
    options.RequestHeaderNames.Clear();
    options.RequestHeaderNames.Add("Idempotency-Key");
    options.ResponseHeaderName = "Idempotency-Key";
    options.ExecutionModeResolver = context =>
        context.Request.Path.StartsWithSegments("/orders/deferred")
            ? InboxExecutionMode.Deferred
            : InboxExecutionMode.Inline;
});

builder.Services.AddSquirrelBoxMule();
builder.Services.AddMule(mule => mule
    .UseInMemory()
    .AddActionsFromAssemblyContaining<CreateDeferredOrderAction>());

var app = builder.Build();

app.UseSquirrelBox();

app.MapGet("/", () => Results.Ok(new
{
    name = "SquirrelBox sample",
    endpoints = new[]
    {
        "POST /orders/inline with Idempotency-Key header replays the completed HTTP response.",
        "POST /orders/computed-key without Idempotency-Key returns a computed key from the DTO fingerprint.",
        "POST /orders/deferred with Idempotency-Key schedules a Mule action that completes the inbox entry.",
        "GET /orders/{id} shows order state.",
        "GET /inbox/{entryId} shows inbox state."
    }
}));

app.MapPost("/orders/inline", async (
    CreateOrderRequest request,
    HttpContext http,
    IInboxService inbox,
    SampleOrderStore orders,
    CancellationToken cancellationToken) =>
{
    var open = await http.OpenSquirrelBoxAsync(inbox, request, cancellationToken: cancellationToken);
    if (!open.Accepted)
        return Results.Conflict(InboxRejectedResponse.From(open));

    var verification = await inbox.VerifyCurrentPayloadAsync(request, cancellationToken);
    if (!verification.Success)
        return Results.Conflict(new { state = verification.State.ToString() });

    var order = orders.MarkCreated(request);
    return Results.Created(
        $"/orders/{order.Id}",
        OrderAcceptedResponse.From(order, open.EffectiveIdempotencyKey, inbox.Current.Entry.Id));
});

app.MapPost("/orders/computed-key", async (
    CreateOrderRequest request,
    HttpContext http,
    IInboxService inbox,
    SampleOrderStore orders,
    CancellationToken cancellationToken) =>
{
    var open = await http.OpenSquirrelBoxAsync(inbox, request, cancellationToken: cancellationToken);
    if (!open.Accepted)
        return Results.Conflict(InboxRejectedResponse.From(open));

    var order = orders.MarkCreated(request);
    return Results.Created(
        $"/orders/{order.Id}",
        OrderAcceptedResponse.From(order, open.EffectiveIdempotencyKey, inbox.Current.Entry.Id));
});

app.MapPost("/orders/deferred", async (
    CreateOrderRequest request,
    HttpContext http,
    IInboxService inbox,
    IInboxMuleScheduler scheduler,
    SampleOrderStore orders,
    CancellationToken cancellationToken) =>
{
    var open = await http.OpenSquirrelBoxAsync(inbox, request, cancellationToken: cancellationToken);
    if (!open.Accepted)
        return Results.Conflict(InboxRejectedResponse.From(open));

    var verification = await inbox.VerifyCurrentPayloadAsync(request, cancellationToken);
    if (!verification.Success)
        return Results.Conflict(new { state = verification.State.ToString() });

    var order = orders.MarkQueued(request);
    var actionId = await scheduler.EnqueueCurrentAsync(
        CreateDeferredOrderAction.Key,
        request,
        cancellationToken: cancellationToken);

    return Results.Accepted(
        $"/orders/{order.Id}",
        DeferredOrderAcceptedResponse.From(order, open.EffectiveIdempotencyKey, inbox.Current.Entry.Id, actionId));
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
/// Mule action that completes deferred orders while preserving the SquirrelBox inbox context.
/// </summary>
[MuleAction("samples.squirrelbox.orders.create.v1")]
public sealed class CreateDeferredOrderAction : SquirrelBoxMuleAction<CreateOrderRequest>
{
    /// <summary>
    /// Gets the Mule action key used by the sample endpoint.
    /// </summary>
    public static ActionKey Key { get; } = ActionKey.From("samples.squirrelbox.orders.create.v1");

    private readonly SampleOrderStore _orders;

    /// <summary>
    /// Initializes a new instance of the <see cref="CreateDeferredOrderAction"/> class.
    /// </summary>
    /// <param name="inbox">The inbox service used by the base action.</param>
    /// <param name="orders">The in-memory sample order store.</param>
    public CreateDeferredOrderAction(IInboxService inbox, SampleOrderStore orders)
        : base(inbox)
    {
        _orders = orders;
    }

    /// <inheritdoc />
    protected override async ValueTask ExecuteInboxAsync(
        SquirrelBoxMuleActionContext<CreateOrderRequest> context,
        CancellationToken cancellationToken)
    {
        await Task.Delay(150, cancellationToken);
        _orders.MarkCompletedByMule(context.Payload);
    }

    /// <inheritdoc />
    protected override ValueTask<InboxCompletion> CreateCompletionAsync(
        SquirrelBoxMuleActionContext<CreateOrderRequest> context,
        CancellationToken cancellationToken)
    {
        var completion = new InboxCompletion
        {
            ResultType = nameof(OrderSnapshot)
        };

        completion.Metadata["order-id"] = context.Payload.ExternalOrderId;
        completion.Metadata["worker"] = "mule";
        return ValueTask.FromResult(completion);
    }
}

/// <summary>
/// In-memory order store used by the sample application.
/// </summary>
public sealed class SampleOrderStore
{
    private readonly ConcurrentDictionary<string, OrderSnapshot> _orders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Marks an order as created inline.
    /// </summary>
    /// <param name="request">The incoming order request.</param>
    /// <returns>The stored order snapshot.</returns>
    public OrderSnapshot MarkCreated(CreateOrderRequest request)
        => Upsert(request, "created");

    /// <summary>
    /// Marks an order as queued for deferred execution.
    /// </summary>
    /// <param name="request">The incoming order request.</param>
    /// <returns>The stored order snapshot.</returns>
    public OrderSnapshot MarkQueued(CreateOrderRequest request)
        => Upsert(request, "queued");

    /// <summary>
    /// Marks a deferred order as completed by Mule.
    /// </summary>
    /// <param name="request">The incoming order request.</param>
    /// <returns>The stored order snapshot.</returns>
    public OrderSnapshot MarkCompletedByMule(CreateOrderRequest request)
        => Upsert(request, "completed-by-mule");

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

    private OrderSnapshot Upsert(CreateOrderRequest request, string status)
    {
        var order = new OrderSnapshot(
            request.ExternalOrderId,
            request.CustomerId,
            request.Amount,
            status,
            DateTimeOffset.UtcNow);

        _orders[order.Id] = order;
        return order;
    }
}

/// <summary>
/// Request used by the sample order endpoints.
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
    /// <param name="order">The stored order snapshot.</param>
    /// <param name="idempotencyKey">The effective idempotency key.</param>
    /// <param name="entryId">The inbox entry id.</param>
    /// <returns>The response DTO.</returns>
    public static OrderAcceptedResponse From(OrderSnapshot order, string? idempotencyKey, Ulid entryId)
        => new(order, idempotencyKey, entryId.ToString());
}

/// <summary>
/// Response returned when a deferred order is accepted.
/// </summary>
/// <param name="Order">The queued order snapshot.</param>
/// <param name="IdempotencyKey">The effective idempotency key.</param>
/// <param name="InboxEntryId">The SquirrelBox inbox entry id.</param>
/// <param name="MuleActionId">The Mule durable action id.</param>
public sealed record DeferredOrderAcceptedResponse(
    OrderSnapshot Order,
    string? IdempotencyKey,
    string InboxEntryId,
    Guid MuleActionId)
{
    /// <summary>
    /// Creates a deferred order response from inbox and Mule identifiers.
    /// </summary>
    /// <param name="order">The queued order snapshot.</param>
    /// <param name="idempotencyKey">The effective idempotency key.</param>
    /// <param name="entryId">The inbox entry id.</param>
    /// <param name="muleActionId">The Mule action id.</param>
    /// <returns>The response DTO.</returns>
    public static DeferredOrderAcceptedResponse From(
        OrderSnapshot order,
        string? idempotencyKey,
        Ulid entryId,
        Guid muleActionId)
        => new(order, idempotencyKey, entryId.ToString(), muleActionId);
}

/// <summary>
/// Response returned when SquirrelBox rejects duplicate or conflicting work.
/// </summary>
/// <param name="State">The inbox open state.</param>
/// <param name="IdempotencyKey">The effective idempotency key, when known.</param>
/// <param name="InboxEntryId">The inbox entry id, when known.</param>
public sealed record InboxRejectedResponse(
    string State,
    string? IdempotencyKey,
    string? InboxEntryId)
{
    /// <summary>
    /// Creates a rejection response from an inbox open result.
    /// </summary>
    /// <param name="open">The open result.</param>
    /// <returns>The response DTO.</returns>
    public static InboxRejectedResponse From(InboxOpenResult open)
        => new(
            open.State.ToString(),
            open.EffectiveIdempotencyKey,
            open.Entry?.Id.ToString());
}
