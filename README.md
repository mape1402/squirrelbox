# SquirrelBox

SquirrelBox is the Elysium inbox/idempotency toolkit for .NET services.

It protects incoming work from accidental duplicate execution across HTTP requests, messaging consumers, and application pipelines. The core is transport-neutral: adapters translate HTTP, messaging, Pigeon, and Mule concepts into the same inbox model.

## Packages

```bash
dotnet add package SquirrelBox
dotnet add package SquirrelBox.InMemory
dotnet add package SquirrelBox.EntityFrameworkCore
dotnet add package SquirrelBox.AspNetCore
dotnet add package SquirrelBox.Messaging
dotnet add package SquirrelBox.Messaging.Pigeon
dotnet add package SquirrelBox.Mule
```

## Getting Started

Register the core and choose one storage provider:

```csharp
using SquirrelBox;
using SquirrelBox.InMemory;

services
    .AddSquirrelBox(options =>
    {
        options.DefaultEntryLifetime = TimeSpan.FromHours(24);
        options.AllowPayloadHashAsIdempotencyKey = true;
        options.ScanAssemblyContaining<OrdersInboxFingerprintProfile>();
    })
    .UseInMemory();
```

Use EF Core for durable storage:

```csharp
using Microsoft.EntityFrameworkCore;
using SquirrelBox.EntityFrameworkCore;

services.AddDbContextFactory<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

services
    .AddSquirrelBox()
    .UseEntityFrameworkInbox<AppDbContext>();

public sealed class AppDbContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplySquirrelBoxInbox();
}
```

## Core Usage

```csharp
var open = await inbox.OpenOrContinueAsync(
    InboxOpenRequest.For(
        source: "http",
        operation: "POST /orders",
        idempotencyKey: "client-key-1",
        payload: request),
    cancellationToken);

if (!open.Accepted)
    return;

try
{
    await handler.Handle(request, cancellationToken);
    await inbox.CompleteCurrentAsync(cancellationToken: cancellationToken);
}
catch (Exception ex)
{
    await inbox.FailCurrentAsync(ex, cancellationToken);
    throw;
}
```

SquirrelBox identifies work by:

```text
Source + Operation + IdempotencyKey
```

Entries use ULID ids and move through:

```text
Started -> Completed
Started -> Failed
Started -> Expired
```

## Declared Operations

HTTP and other synchronous transports can use declared operations when they need inline/deferred execution that can be switched by configuration.

```csharp
public sealed class CreateOrderOperation
    : SquirrelBoxOperation<CreateOrderRequest, OrderCreated>
{
    protected override async ValueTask<OrderCreated> ExecuteAsync(
        CreateOrderRequest request,
        SquirrelBoxOperationContext context,
        CancellationToken cancellationToken)
    {
        var service = context.Services.GetRequiredService<IOrderService>();
        return await service.CreateAsync(request, cancellationToken);
    }
}
```

Invoke the operation from an endpoint:

```csharp
var result = await operations
    .ExecuteAsync<CreateOrderOperation, CreateOrderRequest, OrderCreated>(
        request,
        cancellationToken);

if (result.Deferred)
    return Results.Accepted($"/inbox/{result.Context.Entry.Id}");

if (result.Executed)
    return Results.Created($"/orders/{result.Result.Id}", result.Result);

return Results.Conflict(result.Decision);
```

When the entry is inline, SquirrelBox executes the operation and completes the inbox entry. When the entry is deferred, SquirrelBox stores a durable operation envelope through Mule and releases the current context without completing the inbox entry.

## Payload Fingerprints

When no idempotency key is received, SquirrelBox can compute one from the semantic payload. Profiles let you decide which fields matter.

```csharp
public sealed class OrdersInboxFingerprintProfile : InboxFingerprintProfile
{
    public override void Configure(InboxFingerprintProfileBuilder builder)
    {
        builder.For<CreateOrderRequest>()
            .Use(request => new
            {
                request.CustomerId,
                request.ExternalOrderId,
                request.Amount
            });
    }
}
```

Profiles are discovered from assemblies:

```csharp
services.AddSquirrelBox(options =>
    options.ScanAssemblyContaining<OrdersInboxFingerprintProfile>());
```

## ASP.NET Core

Add the HTTP middleware:

```csharp
using SquirrelBox.AspNetCore;

services.AddSquirrelBoxAspNetCore(options =>
{
    options.RequestHeaderNames.Clear();
    options.RequestHeaderNames.Add("Idempotency-Key");
    options.ResponseHeaderName = "Idempotency-Key";
    options.CaptureCompletedResponses = true;
    options.ReplayCompletedResponses = true;
});

app.UseSquirrelBox();
```

If the request has an idempotency header, the middleware reserves the inbox entry before the endpoint runs. Completed inline responses are captured with status code, body, content type, and configured response headers. A later duplicate completed request replays that stored response instead of entering the endpoint again.

If the request has no header, the middleware does not hash the raw body. Your endpoint can open the inbox after model binding, using the DTO. The response still gets the effective key:

```csharp
app.MapPost("/orders", async (
    CreateOrderRequest request,
    HttpContext http,
    IInboxService inbox,
    CancellationToken cancellationToken) =>
{
    var open = await http.OpenSquirrelBoxAsync(inbox, request, cancellationToken: cancellationToken);
    if (!open.Accepted)
        return Results.Conflict();

    var verification = await inbox.VerifyCurrentPayloadAsync(request, cancellationToken);
    if (!verification.Success)
        return Results.Conflict();

    await handler.Handle(request, cancellationToken);

    return Results.Ok();
});
```

When `UseSquirrelBox()` wraps the endpoint, inline entries owned by the current context are completed by the middleware after the HTTP response has been written to the capture buffer. Declared operations can also own completion when they open the inbox themselves.

## Messaging

Use the neutral messaging adapter when building a transport adapter or orchestration layer:

```csharp
using SquirrelBox.Messaging;

services.AddSquirrelBoxMessaging(options =>
{
    options.IdempotencyKeyMetadataNames.Add("my-idempotency-key");
});

var result = await messages.OpenAsync(new InboxMessageContext
{
    Transport = "rabbitmq",
    Topic = "orders",
    Version = "1.0.0",
    Subscription = "billing",
    Operation = "created",
    MessageId = messageId,
    Payload = payload,
    Metadata = metadata
});

if (result.ShouldExecute)
{
    await consumer.Handle(payload, cancellationToken);
    await inbox.CompleteCurrentAsync(cancellationToken: cancellationToken);
}

messages.AttachEffectiveKey(replyMetadata);
```

The default operation shape is:

```text
topic:version/subscription/operation
```

## Pigeon

`SquirrelBox.Messaging.Pigeon` targets Pigeon 3.1.0 and uses the official consume decision and consume execution pipelines.

```csharp
using Pigeon.Messaging.Contracts;
using SquirrelBox.Messaging.Pigeon;

services.AddSquirrelBoxPigeon(options =>
{
    options.Transport = "pigeon";
    options.MessageIdMetadataName = "message-id";
    options.VersionResolver = context =>
        context.MessageVersion.Major == 0 &&
        context.MessageVersion.Minor == 0 &&
        context.MessageVersion.Patch == 0
            ? SemanticVersion.Default
            : context.MessageVersion;
    options.ExecutionModeResolver = context =>
        context.Topic == "orders.deferred"
            ? InboxExecutionMode.Deferred
            : InboxExecutionMode.Inline;
});
```

The decision interceptor opens the inbox before the consumer handler. It maps SquirrelBox policy to Pigeon decisions:

```text
Continue  -> execute the consumer
Defer     -> schedule a Mule continuation and acknowledge the broker delivery
AckAndSkip -> duplicate already handled or in progress
Retry     -> failed duplicate or retry policy
Reject    -> payload conflict or rejection policy
```

The execution interceptor wraps the real Pigeon handler. On success it completes the inbox entry; on exception it fails the inbox entry. Deferred replay uses `IPigeonConsumerInvoker`, so the same Pigeon consumer pipeline runs later in a new scope.

If Pigeon does not expose a useful `MessageVersion` in the live consume context, `SquirrelBox.Messaging.Pigeon` falls back to `SemanticVersion.Default`. You can override that with `VersionResolver`. For durable replay, the adapter also stores the effective version as envelope metadata so the worker can reconstruct the same route after JSON serialization.

## Mule Deferred Execution

Inline execution is the default. Deferred execution is opt-in and pairs naturally with Mule durable actions.

```csharp
using Mule;
using Mule.InMemory;
using SquirrelBox.Mule;

services.AddSquirrelBoxMule();
services.AddMule(mule => mule
    .UseInMemory()
    .AddActionsFromAssemblyContaining<SquirrelBoxOperationMuleAction>()
    .AddActionsFromAssemblyContaining<SquirrelBoxPigeonMuleAction>());
```

SquirrelBox reserves the inbox entry first. Mule receives metadata with `squirrelbox-inbox-id` and `idempotency-key`, plus a deduplication key based on the inbox entry id when you do not provide one.

For HTTP endpoints that schedule declared operations, open the inbox entry with deferred mode before invoking the operation:

```csharp
var open = await http.OpenSquirrelBoxAsync(
    inbox,
    request,
    executionMode: InboxExecutionMode.Deferred,
    cancellationToken: cancellationToken);

var result = await operations
    .ExecuteAsync<CreateOrderOperation, CreateOrderRequest, OrderCreated>(
        request,
        cancellationToken);
```

The HTTP request returns quickly after scheduling the Mule action. The Mule action later calls `IInboxService.ContinueAsync(entryId)` through the base class and owns the final inbox completion.

## Sample

Run the sample app:

```bash
dotnet run --project samples/SquirrelBox.Sample/SquirrelBox.Sample.csproj --urls http://127.0.0.1:5188
```

Try these flows:

```bash
curl -i -X POST http://127.0.0.1:5188/orders/inline \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: inline-key-1" \
  -d "{\"customerId\":\"cust-1\",\"externalOrderId\":\"inline-1\",\"amount\":42.5}"

curl -i -X POST http://127.0.0.1:5188/orders/computed-key \
  -H "Content-Type: application/json" \
  -d "{\"customerId\":\"cust-2\",\"externalOrderId\":\"computed-1\",\"amount\":10,\"note\":\"ignored\"}"

curl -i -X POST http://127.0.0.1:5188/orders/deferred \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: deferred-key-1" \
  -d "{\"customerId\":\"cust-3\",\"externalOrderId\":\"deferred-1\",\"amount\":99.99}"

curl -i -X POST http://127.0.0.1:5188/pigeon/inline \
  -H "Content-Type: application/json" \
  -d "{\"orderId\":\"pigeon-inline-1\",\"customerId\":\"cust-4\",\"amount\":12.50}"

curl -i -X POST http://127.0.0.1:5188/pigeon/deferred \
  -H "Content-Type: application/json" \
  -d "{\"orderId\":\"pigeon-deferred-1\",\"customerId\":\"cust-5\",\"amount\":15.75}"
```

## Why HTTP Does Not Hash Raw Bodies

Raw body hashing is byte-level idempotency. Two equivalent JSON payloads with different property order can produce different hashes. SquirrelBox prefers semantic hashing from DTOs or explicit fingerprint profiles.

## Testing

The test suite covers:

- Core open/continue, duplicate states, payload conflicts, policies, fingerprints, expiration, completion, and failure.
- In-memory storage.
- ASP.NET Core e2e flows for explicit header response replay and computed keys.
- Messaging e2e flows for metadata keys and computed keys.
- Pigeon decision and execution interceptor behavior, including deferred replay through Mule.
- Mule scheduler metadata propagation, action context rehydration, declared operation continuations, completion/failure handling, and hosted worker e2e execution.
- EF Core SQL Server e2e flows using Docker, including concurrent duplicate reservation and ambient transaction suppression.
