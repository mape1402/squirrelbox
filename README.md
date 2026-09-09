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
});

app.UseSquirrelBox();
```

If the request has an idempotency header, the middleware reserves the inbox entry before the endpoint runs.

If the request has no header, the middleware does not hash the raw body. Your endpoint or Spider pipeline can open the inbox after model binding, using the DTO. The response still gets the effective key:

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

    await handler.Handle(request, cancellationToken);
    await inbox.CompleteCurrentAsync(cancellationToken: cancellationToken);

    return Results.Ok();
});
```

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

`SquirrelBox.Messaging.Pigeon` registers a Pigeon consume interceptor. It reads `ConsumeContext` metadata before `HubConsumer` execution and opens the inbox context inline.

```csharp
using SquirrelBox.Messaging.Pigeon;

services.AddSquirrelBoxPigeon(options =>
{
    options.Transport = "pigeon";
    options.MessageIdMetadataName = "message-id";
});
```

If SquirrelBox policy rejects execution, the interceptor throws `SquirrelBoxPigeonRejectedException`, allowing Pigeon failure/ack configuration to decide broker behavior.

## Mule Deferred Execution

Inline execution is the default. Deferred execution is opt-in and pairs naturally with Mule durable actions.

```csharp
using Mule;
using SquirrelBox.Mule;

services.AddSquirrelBoxMule();

var open = await inbox.OpenOrContinueAsync(
    InboxOpenRequest.For(
        "http",
        "POST /orders",
        idempotencyKey,
        request,
        executionMode: InboxExecutionMode.Deferred),
    cancellationToken);

if (open.State == InboxOpenState.Opened)
{
    await muleInbox.EnqueueCurrentAsync(
        ActionKey.From("orders.create.v1"),
        request,
        cancellationToken: cancellationToken);
}
```

SquirrelBox reserves the inbox entry first. Mule receives metadata with `squirrelbox-inbox-id` and `idempotency-key` so the deferred action can keep traceability.

## Why HTTP Does Not Hash Raw Bodies

Raw body hashing is byte-level idempotency. Two equivalent JSON payloads with different property order can produce different hashes. SquirrelBox prefers semantic hashing from DTOs or explicit fingerprint profiles.

## Testing

The test suite covers:

- Core open/continue, duplicate states, payload conflicts, policies, fingerprints, expiration, completion, and failure.
- In-memory storage.
- ASP.NET Core e2e flows for explicit headers and computed keys.
- Messaging e2e flows for metadata keys and computed keys.
- Pigeon consume interceptor behavior.
- Mule scheduler metadata propagation.
- EF Core SQL Server e2e flows using Docker, including concurrent duplicate reservation and ambient transaction suppression.
