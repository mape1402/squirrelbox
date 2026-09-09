# Pigeon Requirement: Consume Execution Interceptor

> Status: fulfilled by Pigeon 3.1.0. This document is kept as the integration requirement that led to the SquirrelBox Pigeon adapter.

## Context

Pigeon 3.0.0 added the pieces needed for deferred consume flows:

- `IConsumeDecisionInterceptor`
- `PigeonConsumeDecision.Defer`
- `PigeonConsumeEnvelope`
- `IPigeonConsumeEnvelopeFactory`
- `IPigeonConsumerInvoker`
- `ConsumeExecutionSource`
- route-specific interceptor registration with `ForConsumer(...)`

That solves the deferred case cleanly: an interceptor can persist durable work, return `Defer`, and let Pigeon acknowledge the broker delivery without executing the consumer handler.

SquirrelBox can use that path for deferred inbox execution.

The remaining gap is inline idempotency.

## Problem

SquirrelBox needs to support this inline lifecycle around a Pigeon consumer handler:

```text
open inbox
execute consumer handler
if handler succeeds: complete inbox
if handler fails: fail inbox
```

Today Pigeon has:

- `IConsumeInterceptor`, which runs before the handler
- `IConsumeDecisionInterceptor`, which can continue, skip, retry, reject, or defer before the handler

The remaining requirement was a public interceptor that wraps the actual handler execution.

Without an around-handler hook, SquirrelBox has no clean place to complete or fail the inbox after an inline consumer finishes.

The alternatives are not ideal:

- throw exceptions for control flow, which Pigeon 3.0.0 intentionally fixed
- execute the handler manually from an `IConsumeDecisionInterceptor`
- wrap or replace Pigeon consumer registrations
- require application consumers to manually complete SquirrelBox

All of those would make the integration brittle or confusing.

## Requirement

Add a consume execution interceptor that runs around the consumer handler after consume decisions have selected `Continue`.

Suggested API:

```csharp
public delegate ValueTask ConsumeExecutionDelegate(
    ConsumeContext context,
    CancellationToken cancellationToken = default);

public interface IConsumeExecutionInterceptor
{
    ValueTask InvokeAsync(
        ConsumeContext context,
        ConsumeExecutionDelegate next,
        CancellationToken cancellationToken = default);
}
```

## Expected Pipeline Order

For a live broker delivery:

```text
Create ConsumeContext
Run IConsumeInterceptor
Run global IConsumeDecisionInterceptor
Run route-specific IConsumeDecisionInterceptor

If decision != Continue:
  apply decision settlement
  do not run handler
  do not run execution interceptors

If decision == Continue:
  Run global IConsumeExecutionInterceptor around handler
  Run route-specific IConsumeExecutionInterceptor around handler
  Execute configured consumer handler / HubConsumer method
```

The same execution pipeline should be used by `IPigeonConsumerInvoker.InvokeAsync(...)` when replaying a `PigeonConsumeEnvelope`, so deferred replays behave like normal Pigeon consumer executions.

## Registration

Global registration:

```csharp
pigeon.AddConsumeExecutionInterceptor<MyExecutionInterceptor>();
```

Route-specific registration:

```csharp
pigeon.ForConsumer(OrderRoutes.CreatedForBilling)
    .AddConsumeExecutionInterceptor<MyExecutionInterceptor>();
```

The interceptor should be resolved from the same scoped service provider as the `ConsumeContext.Services` and the consumer handler.

## SquirrelBox Use Case

SquirrelBox would use two Pigeon integration points:

1. `IConsumeDecisionInterceptor`
   - opens or continues the inbox
   - detects duplicates
   - maps SquirrelBox policy to `Continue`, `AckAndSkip`, `Retry`, `Reject`, or `Defer`
   - captures `PigeonConsumeEnvelope` and schedules Mule when deferred

2. `IConsumeExecutionInterceptor`
   - completes the inbox after a successful inline handler
   - fails the inbox when the inline handler throws

Example SquirrelBox execution interceptor:

```csharp
public sealed class SquirrelBoxPigeonExecutionInterceptor : IConsumeExecutionInterceptor
{
    private readonly IInboxService _inbox;

    public SquirrelBoxPigeonExecutionInterceptor(IInboxService inbox)
    {
        _inbox = inbox;
    }

    public async ValueTask InvokeAsync(
        ConsumeContext context,
        ConsumeExecutionDelegate next,
        CancellationToken cancellationToken = default)
    {
        // Deferred replay is owned by the durable Mule action that continued the inbox.
        if (context.ExecutionSource == ConsumeExecutionSource.DeferredReplay)
        {
            await next(context, cancellationToken);
            return;
        }

        try
        {
            await next(context, cancellationToken);

            if (_inbox.Current is not null)
                await _inbox.CompleteCurrentAsync(InboxCompletion.Empty, cancellationToken);
        }
        catch (Exception exception)
        {
            if (_inbox.Current is not null)
                await _inbox.FailCurrentAsync(exception, cancellationToken);

            throw;
        }
    }
}
```

## Acceptance Criteria

- Execution interceptors wrap both `HubConsumer` methods and handlers registered with `AddConsumeHandler`.
- Execution interceptors run only when the final consume decision is `Continue`.
- Execution interceptors can observe handler success, handler exceptions, and always run `finally` logic.
- Exceptions thrown by the handler or execution interceptor continue to flow to Pigeon's existing failure and settlement behavior.
- Global and route-specific execution interceptors are supported.
- Route-specific execution interceptors use `PigeonRouteKey` matching with topic, version, and subscription.
- Execution interceptors are resolved from the same DI scope as the consumer handler.
- `IConsumeContextAccessor` remains populated while execution interceptors and the handler run.
- `IPigeonConsumerInvoker.InvokeAsync(...)` uses the same execution interceptor pipeline.
- Existing `IConsumeInterceptor` and `IConsumeDecisionInterceptor` behavior remains unchanged.

## Non-Goals

- This does not replace `IConsumeDecisionInterceptor`.
- This does not change broker settlement decisions.
- This does not require Pigeon to know anything about SquirrelBox.
- This does not require consumers to implement a SquirrelBox-specific contract.

## Why This Matters

This hook allows libraries like SquirrelBox to implement inline idempotency without taking over Pigeon's dispatcher or asking application code to manually complete infrastructure state.

With this addition:

```text
Deferred consume -> IConsumeDecisionInterceptor + PigeonConsumeEnvelope + IPigeonConsumerInvoker
Inline consume   -> IConsumeDecisionInterceptor + IConsumeExecutionInterceptor
```

Both modes stay inside Pigeon's official pipeline, and applications can switch between inline and deferred execution without changing their consumers.
