# Pigeon Deferred Consume Invocation Requirement

> Status: fulfilled by Pigeon 3.x public replay APIs and integrated by `SquirrelBox.Messaging.Pigeon`.

## Context

SquirrelBox can protect messaging consumers by opening an inbox entry before executing the protected work. For inline consumers this works naturally: a Pigeon decision interceptor opens the inbox context, the `HubConsumer` executes inside the same flow, and the inbox entry is completed or failed before the broker delivery is finished.

Deferred execution changes that shape. When the interceptor decides to defer the work, the current broker delivery should persist the inbox reservation and schedule durable work first, then acknowledge the broker message. Later, a background worker must invoke the same Pigeon consumer from a stored envelope, without depending on an external orchestrator or on the original broker delivery scope.

Pigeon does not need to depend on SquirrelBox. Pigeon needs to expose enough public infrastructure to make a consumed message replayable and invocable outside the broker receiver.

## Requirement

Pigeon must expose a public API that allows a consume decision interceptor to:

1. Inspect a broker delivery before the `HubConsumer` executes.
2. Short-circuit the delivery with an explicit result such as continue, ack-and-skip, reject, retry, or defer.
3. Capture the delivery as a serializable consume envelope.
4. Reconstruct a consume context from that envelope later.
5. Invoke the same consumer pipeline from a background worker.

The goal is to support this flow with Pigeon's own replayable consume pipeline:

```text
Broker delivery
  -> Pigeon consume decision interceptor
  -> SquirrelBox opens inbox
  -> if inline: Pigeon executes HubConsumer normally
  -> if deferred: SquirrelBox schedules durable work and Pigeon ACKs after persistence
  -> Durable worker loads the stored envelope
  -> Pigeon reconstructs ConsumeContext
  -> Pigeon invokes the same HubConsumer/pipeline
  -> SquirrelBox completes or fails the inbox entry
```

## Public Capabilities Needed

### 1. Before-Consumer Interceptor

Pigeon should expose an interceptor hook that runs before `HubConsumer` execution and has access to the delivery metadata required to build an idempotency operation.

The interceptor should be able to read:

- Typed payload or raw payload.
- Message id.
- Correlation id.
- Topic.
- Operation.
- Version.
- Subscription or consumer group.
- Metadata/headers.
- Reply metadata, when the transport supports request/reply.

### 2. Explicit Short-Circuit Result

The interceptor should be able to stop the consumer pipeline without throwing an exception for normal control flow.

Suggested result shape:

```csharp
public enum PigeonConsumeDecision
{
    Continue,
    AckAndSkip,
    Reject,
    Retry,
    Defer
}
```

SquirrelBox would map inbox policy outcomes to these decisions:

```text
Opened inline           -> Continue
Opened deferred         -> Defer
Duplicate completed     -> AckAndSkip
Duplicate in progress   -> AckAndSkip or Retry, depending on configuration
Payload conflict        -> Reject
Duplicate failed        -> Retry or Reject, depending on configuration
Missing key             -> Reject or Continue, depending on configuration
```

### 3. Serializable Consume Envelope

Pigeon should expose a transport-neutral envelope that can be persisted by Mule or any durable scheduler.

Suggested contract:

```csharp
public sealed class PigeonConsumeEnvelope
{
    public string MessageId { get; init; }

    public string Topic { get; init; }

    public string Operation { get; init; }

    public string Version { get; init; }

    public string Subscription { get; init; }

    public string CorrelationId { get; init; }

    public string PayloadType { get; init; }

    public byte[] Payload { get; init; }

    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
```

The envelope must contain enough information for Pigeon to rebuild the consume context later and resolve the correct consumer.

### 4. Public Consumer Invoker

Pigeon should expose a public service that invokes the same consumer pipeline from a previously captured envelope.

Suggested contract:

```csharp
public interface IPigeonConsumerInvoker
{
    ValueTask InvokeAsync(
        PigeonConsumeEnvelope envelope,
        CancellationToken cancellationToken = default);
}
```

This invoker should:

- Create the expected DI scope.
- Deserialize or bind the payload.
- Rebuild `ConsumeContext`.
- Restore metadata and correlation values.
- Resolve the same `HubConsumer`.
- Execute the same consumer pipeline used by live broker deliveries.

### 5. Reconstructable Consume Context

If consumers depend on an ambient consume context, Pigeon should support setting that context during invoker execution.

Example:

```csharp
public interface IPigeonConsumeContextAccessor
{
    ConsumeContext Current { get; set; }
}
```

The context must behave the same whether the message came from a broker receiver or from `IPigeonConsumerInvoker`.

### 6. ACK/NACK Boundary for Deferred Work

For deferred execution, Pigeon must allow this ordering:

```text
Open inbox entry
Persist durable action/envelope
Commit durable work
ACK broker delivery
```

If inbox reservation or durable action persistence fails, Pigeon must not acknowledge the broker message.

This prevents message loss and guarantees the inbox entry exists before duplicate deliveries can pass through the system.

### 7. Reply Metadata Mutation

For request/reply or orchestration flows, Pigeon should allow metadata to be attached to outgoing replies.

SquirrelBox needs to propagate:

```text
idempotency-key = <effective-key>
squirrelbox-inbox-id = <ulid>
```

This gives callers a stable trace id/key even when the key was computed or normalized inside the consumer flow.

## Acceptance Criteria

SquirrelBox should be able to provide a Pigeon adapter that works through Pigeon's public consume pipeline:

```csharp
services.AddSquirrelBoxPigeon(options =>
{
    options.ExecutionMode = InboxExecutionMode.Deferred;
});
```

The adapter must be able to:

- Receive a message through Pigeon.
- Open a SquirrelBox inbox entry before `HubConsumer` execution.
- Persist a durable action/envelope when deferred execution is selected.
- ACK the broker message only after the durable work is persisted.
- Reinvoke the same `HubConsumer` later from a Mule worker.
- Rebuild the same consumer metadata/context visible to application code.
- Complete the SquirrelBox inbox entry when the worker succeeds.
- Fail the SquirrelBox inbox entry when the worker fails.
- Attach the effective idempotency key to outgoing reply metadata when a reply is produced.

## Impact Assessment

### Low Impact

The change is low impact if Pigeon already has an internal consumer invoker and the broker receiver already delegates execution to that invoker. In that case, the main work is making the invoker and envelope public and supported.

### Medium Impact

The change is medium impact if Pigeon has interceptors today, but `HubConsumer` execution is still coupled to live broker delivery details. Pigeon would need to extract a reusable invocation layer and a serializable envelope.

### High Impact

The change is high impact if broker ACK/NACK, scope creation, context access, metadata, and `HubConsumer` execution are all mixed in the same transport receiver path. In that case, Pigeon should separate:

```text
Transport receiver
Consume envelope creation
Consumer pipeline
Consumer invoker
ACK/NACK policy
Consume context accessor
Reply metadata writer
```

## Non-Goals

- Pigeon should not take a direct dependency on SquirrelBox.
- Pigeon should not take a direct dependency on Mule.
- Pigeon should not require an external orchestrator for deferred consumer execution.
- Pigeon should not require exceptions for normal short-circuit decisions.

## Summary

The core requirement is not "Pigeon must implement inbox." The requirement is:

```text
Pigeon must make consumed messages replayable and its consumer pipeline invocable outside the original broker delivery.
```

Once that exists, SquirrelBox can provide the idempotency policy and Mule can provide durable scheduling without forcing applications to adopt another orchestration layer.
