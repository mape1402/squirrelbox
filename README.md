# SquirrelBox

SquirrelBox is an Elysium inbox/idempotency toolkit for .NET services.

It protects incoming work from accidental duplicate execution across HTTP requests, messaging consumers, and application pipelines. The core package owns the transport-neutral inbox model; adapters plug that model into ASP.NET Core, Pigeon, and TurtlePath/Spider.

## Packages

```bash
dotnet add package SquirrelBox
dotnet add package SquirrelBox.InMemory
dotnet add package SquirrelBox.EntityFrameworkCore
dotnet add package SquirrelBox.AspNetCore
dotnet add package SquirrelBox.Pigeon
dotnet add package TurtlePath.SquirrelBox
```

## Core Scope

The core package is transport-neutral. It owns:

- inbox identity with `Source + Operation + IdempotencyKey`
- payload hashing for conflict detection
- entry lifecycle with `Started`, `Completed`, `Failed`, and `Expired`
- begin results for duplicates, conflicts, and expired entries
- optional completion snapshots for replay-capable adapters
- failure details for diagnostics
- inline execution by default, with deferred execution modeled as an explicit opt-in mode
- storage contracts implemented by provider packages

The core does not know about HTTP status codes, broker acknowledgements, Spider pipelines, or background execution.

## Intended Shape

- `SquirrelBox`: core abstractions, lifecycle, payload hashing, and idempotency service.
- `SquirrelBox.InMemory`: in-memory store for tests, samples, and lightweight local behavior.
- `SquirrelBox.EntityFrameworkCore`: durable EF Core store for production workloads.
- `SquirrelBox.AspNetCore`: HTTP idempotency adapter.
- `SquirrelBox.Pigeon`: messaging consume adapter.
- `TurtlePath.SquirrelBox`: TurtlePath/Spider pipeline integration.

Inbox execution is inline by default. Deferred execution is planned as an explicit opt-in feature because it changes HTTP and messaging semantics.

## Minimal Usage

```csharp
services.AddSquirrelBox()
        .UseInMemory();
```

```csharp
var inbox = serviceProvider.GetRequiredService<IInbox>();

var result = await inbox.BeginAsync(
    InboxRequest.For(
        source: "http",
        operation: "POST /orders",
        idempotencyKey: request.Headers["Idempotency-Key"],
        payload: createOrderRequest),
    cancellationToken);

if (!result.Accepted)
    return;

try
{
    await handler.Handle(createOrderRequest, cancellationToken);
    await inbox.CompleteAsync(result.Entry.Id, cancellationToken: cancellationToken);
}
catch (Exception ex)
{
    await inbox.FailAsync(result.Entry.Id, ex, cancellationToken);
    throw;
}
```
