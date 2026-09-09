# SquirrelBox

SquirrelBox is an Elysium inbox/idempotency toolkit for .NET services.

It protects incoming work from accidental duplicate execution across HTTP requests, messaging consumers, and application pipelines. The core package is transport-neutral: it owns the inbox model and execution context, while adapters translate HTTP, messaging, and Spider/TurtlePath concepts into that model.

## Packages

```bash
dotnet add package SquirrelBox
dotnet add package SquirrelBox.InMemory
dotnet add package SquirrelBox.EntityFrameworkCore
dotnet add package SquirrelBox.AspNetCore
dotnet add package SquirrelBox.Messaging
dotnet add package TurtlePath.SquirrelBox
```

## Package Shape

- `SquirrelBox`: core inbox service, context, lifecycle, payload hashing, and storage contracts.
- `SquirrelBox.InMemory`: in-memory store for tests, samples, and lightweight local behavior.
- `SquirrelBox.EntityFrameworkCore`: durable EF Core store shell for production storage.
- `SquirrelBox.AspNetCore`: HTTP idempotency adapter shell.
- `SquirrelBox.Messaging`: transport-neutral messaging adapter shell.
- `TurtlePath.SquirrelBox`: TurtlePath/Spider pipeline adapter shell.

`SquirrelBox.Messaging.Pigeon` is intentionally reserved for a future Pigeon-specific adapter. Pigeon should not own the general inbox concept.

## Core Concepts

SquirrelBox identifies an incoming operation with:

```text
Source + Operation + IdempotencyKey
```

Examples:

```text
http      + POST /orders                  + Idempotency-Key
messaging + orders.created:1.0.0/billing  + MessageId
spider    + CreateOrderCommand            + computed payload hash
```

Every inbox entry has a ULID id, an effective idempotency key, a key source, and a lifecycle status:

```text
Started
Completed
Failed
Expired
```

The key source tells adapters whether the key came from the caller or was computed by SquirrelBox:

```text
Explicit
ComputedFromPayload
```

Adapters should propagate the effective key. For HTTP, that usually means returning a response header such as `Idempotency-Key`. For messaging, that usually means attaching metadata such as `idempotency-key` to a reply or orchestration event.

## Inline By Default

SquirrelBox protects the current execution inline by default. It does not move work to the background unless an adapter explicitly opts into deferred execution.

That distinction matters:

```text
Inline inbox      = protect this execution from duplicate processing.
Deferred inbox    = intake plus later execution, a different semantic mode.
```

## Ambient Context

The core service owns an ambient inbox context. This lets an outer adapter open the inbox once and inner code continue it without registering a duplicate.

Typical HTTP plus Spider flow:

```text
HTTP middleware opens context from Idempotency-Key
controller calls Spider
TurtlePath/Spider continues the current context
Spider verifies the deserialized DTO payload
HTTP completes the inbox entry and can emit the effective key
```

Typical messaging plus Spider flow:

```text
Messaging adapter opens context from message metadata
HubConsumer calls Spider
TurtlePath/Spider continues the current context
Messaging adapter completes or fails the inbox entry and handles ack semantics
```

When HTTP has no idempotency header, the HTTP adapter should not hash the raw body by default. Raw body hashing is byte-level idempotency, so equivalent JSON with different property order would produce a different hash. In that case, Spider/TurtlePath can open the context later from the already deserialized DTO and SquirrelBox can return the computed key for traceability.

## Minimal Usage

```csharp
using Microsoft.Extensions.DependencyInjection;
using SquirrelBox;
using SquirrelBox.InMemory;

services.AddSquirrelBox()
        .UseInMemory();
```

```csharp
var inbox = serviceProvider.GetRequiredService<IInboxService>();

var open = await inbox.OpenOrContinueAsync(
    InboxOpenRequest.For(
        source: "http",
        operation: "POST /orders",
        idempotencyKey: "client-key-1",
        payload: createOrderRequest),
    cancellationToken);

if (!open.Accepted)
    return;

try
{
    await handler.Handle(createOrderRequest, cancellationToken);

    await inbox.CompleteCurrentAsync(
        new InboxCompletion
        {
            ContentType = "application/json"
        },
        cancellationToken);
}
catch (Exception ex)
{
    await inbox.FailCurrentAsync(ex, cancellationToken);
    throw;
}
```

## Payload Fallback

If no idempotency key is provided and payload fallback is enabled, SquirrelBox computes a semantic payload hash and uses that as the effective idempotency key.

```csharp
var open = await inbox.OpenOrContinueAsync(
    InboxOpenRequest.For(
        source: "spider",
        operation: "CreateOrderCommand",
        payload: command),
    cancellationToken);

var effectiveKey = open.EffectiveIdempotencyKey;
```

This is useful after a transport or framework has already deserialized the payload into a DTO.

## Payload Verification

An adapter may open context before it has a deserialized payload. Later, inner code can verify or attach the payload hash:

```csharp
await inbox.OpenOrContinueAsync(new InboxOpenRequest
{
    Source = "http",
    Operation = "POST /orders",
    IdempotencyKey = "client-key-1",
    Owner = "http"
}, cancellationToken);

var verification = await inbox.VerifyCurrentPayloadAsync(command, cancellationToken);

if (verification.State == InboxPayloadVerificationState.PayloadConflict)
{
    // Same idempotency key, different semantic payload.
}
```

## Configuration

```csharp
services.AddSquirrelBox(options =>
{
    options.DefaultExecutionMode = InboxExecutionMode.Inline;
    options.AllowPayloadHashAsIdempotencyKey = true;
    options.DefaultEntryLifetime = TimeSpan.FromHours(24);
    options.DefaultOwner = "manual";
});
```

## Testing

The current test suite covers the core service, in-memory store, duplicate states, payload conflicts, computed keys, ambient context continuation, failure/completion, expiration, and simple e2e flows for HTTP/Spider and messaging-style usage.
