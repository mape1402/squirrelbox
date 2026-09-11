# Changelog

All notable changes to SquirrelBox will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [v2.1.0] - 2026-09-11

### Changed

- Updated `SquirrelBox.Messaging.Pigeon` to Pigeon 4.0.0.
- Changed the Pigeon outbox integration to persist Pigeon's prepared `PigeonPublishEnvelope` through `IPigeonPublishEnvelopeFactory`.
- Changed Pigeon outbox replay to publish through `IPigeonPublisherInvoker`, avoiding producer interceptor reruns and reflection-based replay.
- Updated the sample app and README to document the Pigeon 4 outbox flow.

### Fixed

- Kept replay compatibility for legacy `SquirrelBoxPigeonOutboxPayload` envelopes while routing them through the Pigeon 4 publisher invoker.
- Removed obsolete manual Pigeon publish payload reconstruction from the adapter.

## [v2.0.0] - 2026-09-10

### Added

- Added transport-agnostic Outbox core with `IOutboxService`, `IOutboxStore`, `IOutboxTransportPublisher`, `OutboxEnvelope`, outbox profiles, JSON payload serialization, and ULID envelope identity.
- Added InMemory and Entity Framework Core Outbox storage, including SQL Server e2e coverage and diagnostics queries.
- Added Mule Outbox execution with `squirrelbox.outbox.publish.v1`, durable outbox envelope references, and hosted worker e2e tests.
- Added Pigeon publish/outbox adapter that persists normal and raw Pigeon publish operations through SquirrelBox Outbox and later republishes through Pigeon.
- Added SquirrelBox event stream contracts and in-memory event sink for live Inbox, Outbox, and Deferred Work events.
- Added persisted Inbox diagnostics queries for dashboard history without polling.
- Added `SquirrelBox.AspNetCore.Dashboard` with a modern event-driven dashboard, SSE live updates, root user authentication, ASP.NET Core auth mode, and custom auth adapter support.
- Added sample scenarios for HTTP operations producing outbox work, direct outbox enqueue, Pigeon publish through SquirrelBox Outbox, and dashboard usage.
- Added build, package, downloads, and license badges to the README.

## [v1.1.0] - 2026-09-09

### Added

- Introduced the `SquirrelBox` core inbox package with ULID entry identity, ambient inbox context, open-or-continue lifecycle, payload verification, completion data, failure data, expiration, and inline/deferred execution modeling.
- Added declared operation support with `SquirrelBoxOperation<TRequest,TResult>`, `ISquirrelBoxOperationService`, operation discovery, durable operation envelopes, and inline/deferred switching without captured delegates.
- Added `IInboxService.ReleaseCurrent` for deferred transports that schedule durable work without completing the inbox entry in the ingress scope.
- Added transport-neutral policies for duplicate completed, duplicate in-progress, duplicate failed, payload conflict, expired, and missing idempotency key outcomes.
- Added semantic payload fingerprint profiles with assembly discovery, allowing payload hashes to be based on selected fields instead of raw JSON bodies.
- Added transaction-suppressed inbox storage execution so reservations are persisted before protected business work and outside ambient application transactions.
- Added `SquirrelBox.InMemory` for tests, samples, and local development.
- Added `SquirrelBox.EntityFrameworkCore` with SQL Server e2e coverage, EF model configuration, durable storage, and unique reservation enforcement on source, operation, and idempotency key.
- Added `SquirrelBox.AspNetCore` middleware and endpoint helpers for explicit HTTP idempotency headers, DTO-based computed keys, captured inline HTTP completions, and duplicate completed response replay.
- Added `SquirrelBox.Messaging` for transport-neutral message contexts, topic/version/subscription operation resolution, metadata key discovery, and reply metadata propagation.
- Added `SquirrelBox.Messaging.Pigeon` with Pigeon 3.1 consume decision and execution interceptors, message version resolution, inline completion/failure handling, and deferred replay through `IPigeonConsumerInvoker`.
- Added `IInboxService.ContinueAsync` for rehydrating persisted inbox entries by ULID in worker scopes.
- Added `SquirrelBox.Mule` scheduler integration for tying Mule durable actions to the current SquirrelBox inbox context.
- Added `SquirrelBoxOperationMuleAction` for replaying declared operation envelopes through Mule.
- Added `SquirrelBoxMuleAction<TPayload>` for Mule workers that continue, complete, and fail deferred inbox entries from Mule metadata.
- Added a runnable `SquirrelBox.Sample` app showing HTTP declared operations, computed keys, Mule deferred operations, and Pigeon inline/deferred consumers.
- Added XML documentation generation and summary comments for public package APIs.
