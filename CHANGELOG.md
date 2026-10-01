# Changelog

All notable changes to SquirrelBox will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [v3.2.0] - 2026-10-01

### Added

- Added per-entrypoint deferred retry settings for inbox policies, HTTP payload endpoints, and messaging policy profiles.
- Added `Retrying` inbox status plus `IInboxService.RetryCurrentAsync` so non-terminal Mule failures no longer mark deferred inbox entries as terminal failures.
- Added Mule lane/retry auto-configuration from SquirrelBox inbox policies, including retry metadata on durable actions.
- Added active-entry `InProgressTimeout` handling so long-running or retrying deferred work is not reopened by normal idempotency TTL, but can be recovered explicitly after a configured timeout.
- Added SQL Server, Mule worker, HTTP payload, messaging profile, and core unit/e2e coverage for deferred retry policies.

### Changed

- Changed deferred Mule action failure handling to keep entries in `Retrying` while Mule still has attempts available, and mark `Failed` only when the durable action reaches its terminal attempt.
- Changed operation and Pigeon deferred scheduling failures to mark the current inbox entry as failed immediately when the durable action cannot be scheduled.
- Updated the sample app and README to show deferred inbox policies with lanes, retry attempts, backoff, and in-progress timeout.

## [v3.1.0] - 2026-10-01

### Added

- Added entrypoint idempotency policies with configurable entry lifetime, named core policy selection, and completed-duplicate lock behavior.
- Added inline policy configuration to method-level `[SquirrelBoxPayload]` and Minimal API `.WithSquirrelBoxPayload(...)`.
- Added messaging inbox policy profiles discovered from configured assemblies, with topic, version, subscription, and operation matching.
- Added sample endpoints for unmarked HTTP requests, short idempotency windows, completed-forever locking, and profile-based Pigeon inbox activation.

### Changed

- Changed ASP.NET Core inbox activation so `UseSquirrelBox()` no longer opens inbox entries globally or merely because an idempotency header exists; endpoints must opt in explicitly.
- Changed expired and failed inbox entries to reopen for execution instead of blocking retries forever.
- Changed completed entries to block only for the configured idempotency window unless the entrypoint explicitly requests `InboxCompletedLockMode.Forever`.
- Changed `SquirrelBox.Messaging` and the Pigeon consume adapter to skip inbox opening when no messaging inbox policy profile matches.
- Changed HTTP rejection payloads to serialize inbox state and action as names instead of numeric enum values.

## [v3.0.0] - 2026-09-29

### Added

- Added the transport-neutral `SquirrelBoxMetadata` model and the `SquirrelBoxMetadata` messaging section for identity propagation.

### Changed

- Changed `SquirrelBox.Messaging` and `SquirrelBox.Messaging.Pigeon` to read and write SquirrelBox identity metadata as one structured section instead of separate flat metadata keys.
- Changed Pigeon outbox replay to restore the structured `SquirrelBoxMetadata` section from the durable outbox envelope when publishing through Pigeon.

## [v2.4.0] - 2026-09-29

### Added

- Added method-level `[SquirrelBoxPayload]` for MVC actions so HTTP inbox entries can open from bound DTO payloads after model binding.
- Added `.WithSquirrelBoxPayload()` for Minimal API endpoints with the same payload-based idempotency behavior.
- Added ASP.NET Core e2e coverage for computed payload keys, duplicate replay, payload conflicts, custom argument names, and identity response headers.

### Changed

- Changed `UseSquirrelBox()` to defer the HTTP inbox open step for payload-aware endpoints so explicit idempotency keys can still be verified against the bound payload.
- Updated the sample and README to show payload-aware HTTP endpoints without manual inbox calls inside endpoint bodies.

## [v2.3.0] - 2026-09-28

### Added

- Added SquirrelBox operation/attempt identity metadata with stable `IdempotencyKey` + `CorrelationId` and per-attempt `AttemptId` + `TraceId`.
- Added default and replaceable `ICorrelationIdFactory`, `ITraceIdFactory`, and `IAttemptIdFactory` implementations.
- Added `ISquirrelBoxIdentityAccessor` and scoped identity metadata exposure for services running inside an accepted inbox scope.
- Added `SquirrelBoxMessageMetadata` and `ISquirrelBoxMessageMetadataEnricher` for transport-neutral messaging metadata propagation.
- Added EF Core persistence for inbox attempt records so duplicate attempts keep their own trace and attempt ids while preserving the original operation correlation id.

### Changed

- Changed HTTP idempotency responses to propagate correlation id, attempt id, and trace id headers alongside the effective idempotency key.
- Changed HTTP and messaging adapters to reuse incoming configured metadata/header names when returning effective identity values.
- Changed messaging replies and Pigeon consume decisions to attach complete SquirrelBox metadata instead of only the idempotency key.
- Changed Pigeon outbox interception to enrich persisted publish envelopes with the current SquirrelBox identity metadata.
- Updated README guidance for identity metadata, custom factories, HTTP headers, messaging metadata, and Pigeon publish enrichment.

## [v2.2.1] - 2026-09-24

### Changed

- Updated Mule durable action dependencies to `Mule.DurableActions` 1.5.0 and `Mule.DurableActions.InMemory` 1.5.0.
- Updated package reference examples in the README for the patch release.

## [v2.2.0] - 2026-09-23

### Changed

- Changed storage provider registration to hang from the `AddSquirrelBox()` builder, including `.UseEntityFramework<TDbContext>()` and `.UseInMemory()`, keeping DbContext integration in dependency registration.
- Changed the EF model builder and DbContext options hooks to internal infrastructure so applications do not configure SquirrelBox through `OnModelCreating` or standalone EF model APIs.
- Updated the README with the builder-based storage registration flow and a short migration note for older storage registration code.

## [v2.1.0] - 2026-09-11

### Changed

- Updated `SquirrelBox.Messaging.Pigeon` to Pigeon 4.0.0.
- Changed the Pigeon outbox integration to persist Pigeon's prepared `PigeonPublishEnvelope` through `IPigeonPublishEnvelopeFactory`.
- Changed Pigeon outbox replay to publish through `IPigeonPublisherInvoker`, avoiding producer interceptor reruns and reflection-based replay.
- Changed Entity Framework Core registration to inject the SquirrelBox model automatically through the registered `DbContextOptions`, so application `DbContext` types no longer need SquirrelBox model calls in `OnModelCreating`.
- Updated the sample app and README to document the Pigeon 4 outbox flow.

### Fixed

- Kept replay compatibility for legacy `SquirrelBoxPigeonOutboxPayload` envelopes while routing them through the Pigeon 4 publisher invoker.
- Removed obsolete manual Pigeon publish payload reconstruction from the adapter.
- Fixed EF inbox/outbox stores to create contexts through SquirrelBox's model-aware factory instead of assuming the application model already contains SquirrelBox entities.

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
