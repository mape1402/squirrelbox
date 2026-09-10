# SquirrelBox

[![Build](https://github.com/mape1402/squirrelbox/actions/workflows/build-and-release.yml/badge.svg)](https://github.com/mape1402/squirrelbox/actions/workflows/build-and-release.yml)
[![NuGet](https://img.shields.io/nuget/v/SquirrelBox.svg)](https://www.nuget.org/packages/SquirrelBox)
[![Downloads](https://img.shields.io/nuget/dt/SquirrelBox.svg)](https://www.nuget.org/packages/SquirrelBox)
[![License](https://img.shields.io/github/license/mape1402/squirrelbox.svg)](LICENSE)

SquirrelBox is the Elysium inbox/outbox toolkit for .NET services.

It protects incoming work from duplicate execution with the inbox pattern, persists outgoing work with the outbox pattern, and uses Mule durable actions for deferred execution. The core is transport-neutral: HTTP, messaging, Pigeon, custom transports, and dashboard diagnostics all use the same model.

## Packages

```bash
dotnet add package SquirrelBox
dotnet add package SquirrelBox.InMemory
dotnet add package SquirrelBox.EntityFrameworkCore
dotnet add package SquirrelBox.AspNetCore
dotnet add package SquirrelBox.AspNetCore.Dashboard
dotnet add package SquirrelBox.Messaging
dotnet add package SquirrelBox.Messaging.Pigeon
dotnet add package SquirrelBox.Mule
```

Package reference example:

```xml
<PackageReference Include="SquirrelBox" Version="2.0.0" />
<PackageReference Include="SquirrelBox.AspNetCore" Version="2.0.0" />
<PackageReference Include="SquirrelBox.AspNetCore.Dashboard" Version="2.0.0" />
<PackageReference Include="SquirrelBox.EntityFrameworkCore" Version="2.0.0" />
<PackageReference Include="SquirrelBox.Mule" Version="2.0.0" />
```

## Getting Started

Register core services, choose storage, and add Mule when you want durable deferred execution:

```csharp
using Mule.InMemory;
using SquirrelBox;
using SquirrelBox.EntityFrameworkCore;
using SquirrelBox.Mule;

services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

services
    .AddSquirrelBox(options =>
    {
        options.DefaultEntryLifetime = TimeSpan.FromHours(24);
        options.AllowPayloadHashAsIdempotencyKey = true;
        options.ScanAssemblyContaining<OrdersFingerprintProfile>();
    })
    .UseEntityFramework<AppDbContext>();

services.AddSquirrelBoxMule();
services.AddMule(mule => mule
    .UseInMemory()
    .AddActionsFromAssemblyContaining<SquirrelBoxOutboxMuleAction>());
```

SquirrelBox augments the registered `AppDbContext` automatically. Your application `DbContext`
does not need SquirrelBox `DbSet` properties or `OnModelCreating` changes.

Use `UseInMemory()` instead of EF for tests, samples, and local development.

## Inbox

The inbox pattern identifies incoming work by:

```text
Source + Operation + IdempotencyKey
```

Basic usage:

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

Entries use ULID ids and move through `Started`, `Completed`, `Failed`, and `Expired`.

## Declared Operations

Declared operations are the durable-safe path for switching HTTP or other synchronous entry points between inline and deferred execution.

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

Invoke from an endpoint:

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

When execution is deferred, SquirrelBox stores the inbox entry first, schedules a Mule durable action, and completes/fails the inbox later from a worker scope.

## Outbox

The outbox pattern persists outgoing work before it is delivered by a transport.

```csharp
var envelope = await outbox.EnqueueAsync(new OutboxEnqueueRequest
{
    Transport = "pigeon",
    Operation = "orders.created",
    Destination = "orders",
    Payload = new OrderCreated(orderId),
    CorrelationId = correlationId
}, cancellationToken);
```

SquirrelBox stores an `OutboxEnvelope` with:

```text
Ulid Id
Transport
Operation
Destination
PayloadType
Payload
Headers
Metadata
CorrelationId
Status
```

Mule later executes `squirrelbox.outbox.publish.v1`, loads the envelope by ULID, resolves the matching `IOutboxTransportPublisher`, and marks the envelope as `Published` or `Failed`.

Custom publisher:

```csharp
public sealed class MyPublisher : IOutboxTransportPublisher
{
    public string Transport => "my-transport";

    public async ValueTask<OutboxPublishResult> PublishAsync(
        OutboxEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        await client.SendAsync(envelope.Payload, cancellationToken);
        return OutboxPublishResult.Success;
    }
}
```

## Profiles

Inbox fingerprints and outbox profiles are discovered from scanned assemblies.

```csharp
public sealed class OrdersFingerprintProfile : InboxFingerprintProfile
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

Outbox profiles can customize how enqueue requests become durable envelopes without adding noisy fluent configuration.

## ASP.NET Core

Add HTTP idempotency:

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

If a request has an idempotency header, middleware reserves the inbox entry before the endpoint runs. If the request does not have a header, your endpoint can open SquirrelBox after model binding so the computed key is based on the DTO instead of raw body bytes.

## Dashboard

Add the event-driven dashboard:

```csharp
using SquirrelBox.AspNetCore.Dashboard;

services.AddSquirrelBoxDashboard(options =>
{
    options.Authentication.RootUser.Username = "admin";
    options.Authentication.RootUser.Password = "<from-secret-store>";
});

app.MapSquirrelBoxDashboard("/squirrelbox");
```

The dashboard:

- Loads initial Inbox/Outbox history from storage.
- Receives live events through Server-Sent Events.
- Does not poll the database.
- Shows Inbox, Outbox, Deferred Work, and live Events.
- Uses the SquirrelBox logo palette.
- Is closed by default: root user, ASP.NET Core auth, or custom auth must be configured.

ASP.NET Core auth mode:

```csharp
services.AddSquirrelBoxDashboard(options =>
{
    options.Authentication.Mode = SquirrelBoxDashboardAuthenticationMode.AspNetCoreAuthentication;
});

app.MapSquirrelBoxDashboard("/squirrelbox")
   .RequireAuthorization("SquirrelBoxDashboard");
```

Custom auth mode:

```csharp
services.AddSingleton<ISquirrelBoxDashboardAuthenticator, MyDashboardAuthenticator>();
services.AddSquirrelBoxDashboard(options =>
{
    options.Authentication.Mode = SquirrelBoxDashboardAuthenticationMode.Custom;
});
```

## Messaging

Use `SquirrelBox.Messaging` when building a transport adapter or orchestration layer:

```csharp
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

`SquirrelBox.Messaging.Pigeon` targets Pigeon 3.1.0 and integrates with consume and publish interceptors.

```csharp
services.AddSquirrelBoxPigeon(options =>
{
    options.Transport = "pigeon";
    options.EnableOutbox = true;
    options.ExecutionModeResolver = context =>
        context.Topic == "orders.deferred"
            ? InboxExecutionMode.Deferred
            : InboxExecutionMode.Inline;
});
```

Consume:

- Decision interceptor opens the inbox before the consumer handler.
- Execution interceptor completes or fails the inbox after the handler.
- Deferred replay uses `IPigeonConsumerInvoker`.

Publish:

- Publish decision interceptor persists a prepared Pigeon payload in SquirrelBox Outbox.
- Pigeon publish is skipped inline after the envelope is durable.
- Mule later publishes through Pigeon without rerunning producer interceptors.
- Normal and raw publish flows are supported.

## Mule

Register SquirrelBox actions with Mule:

```csharp
services.AddSquirrelBoxMule();
services.AddMule(mule => mule
    .UseInMemory()
    .AddActionsFromAssemblyContaining<SquirrelBoxOutboxMuleAction>()
    .AddActionsFromAssemblyContaining<SquirrelBoxPigeonMuleAction>());
```

SquirrelBox attaches durable metadata such as `squirrelbox-inbox-id`, `squirrelbox-outbox-id`, and `idempotency-key`.

## Sample

Run the sample app:

```bash
dotnet run --project samples/SquirrelBox.Sample/SquirrelBox.Sample.csproj --urls http://127.0.0.1:5188
```

Try:

```bash
curl -i -X POST http://127.0.0.1:5188/orders/inline \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: inline-key-1" \
  -d "{\"customerId\":\"cust-1\",\"externalOrderId\":\"inline-1\",\"amount\":42.5}"

curl -i -X POST http://127.0.0.1:5188/orders/deferred \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: deferred-key-1" \
  -d "{\"customerId\":\"cust-3\",\"externalOrderId\":\"deferred-1\",\"amount\":99.99}"

curl -i -X POST http://127.0.0.1:5188/pigeon/inline \
  -H "Content-Type: application/json" \
  -d "{\"orderId\":\"pigeon-inline-1\",\"customerId\":\"cust-4\",\"amount\":12.50}"

curl -i -X POST http://127.0.0.1:5188/outbox/direct \
  -H "Content-Type: application/json" \
  -d "{\"orderId\":\"audit-1\",\"reason\":\"manual-check\"}"
```

Open the dashboard at `http://127.0.0.1:5188/squirrelbox` with `admin / secret`.

## Testing

The test suite covers:

- Core inbox lifecycle, policies, fingerprints, operation execution, and outbox publication.
- In-memory inbox/outbox storage.
- ASP.NET Core idempotency and dashboard auth/state.
- Messaging and Pigeon consume/publish adapters.
- Mule deferred inbox and outbox execution.
- EF Core SQL Server e2e tests using Docker.

Run everything:

```bash
dotnet test SquirrelBox.slnx -c Debug
```
