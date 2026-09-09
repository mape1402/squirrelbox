# Changelog

All notable changes to SquirrelBox will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [v0.1.0] - 2026-09-09

### Added

- Introduced the `SquirrelBox` core inbox package with ULID entry identity, ambient inbox context, open-or-continue lifecycle, payload verification, completion data, failure data, expiration, and inline/deferred execution modeling.
- Added transport-neutral policies for duplicate completed, duplicate in-progress, duplicate failed, payload conflict, expired, and missing idempotency key outcomes.
- Added semantic payload fingerprint profiles with assembly discovery, allowing payload hashes to be based on selected fields instead of raw JSON bodies.
- Added transaction-suppressed inbox storage execution so reservations are persisted before protected business work and outside ambient application transactions.
- Added `SquirrelBox.InMemory` for tests, samples, and local development.
- Added `SquirrelBox.EntityFrameworkCore` with SQL Server e2e coverage, EF model configuration, durable storage, and unique reservation enforcement on source, operation, and idempotency key.
- Added `SquirrelBox.AspNetCore` middleware and endpoint helpers for explicit HTTP idempotency headers, DTO-based computed keys, captured inline HTTP completions, and duplicate completed response replay.
- Added `SquirrelBox.Messaging` for transport-neutral message contexts, topic/version/subscription operation resolution, metadata key discovery, and reply metadata propagation.
- Added `SquirrelBox.Messaging.Pigeon` with a Pigeon consume interceptor that opens SquirrelBox inbox contexts from message metadata before `HubConsumer` execution.
- Added `IInboxService.ContinueAsync` for rehydrating persisted inbox entries by ULID in worker scopes.
- Added `SquirrelBox.Mule` scheduler integration for tying Mule durable actions to the current SquirrelBox inbox context.
- Added `SquirrelBoxMuleAction<TPayload>` for Mule workers that continue, complete, and fail deferred inbox entries from Mule metadata.
- Added a runnable `SquirrelBox.Sample` app showing HTTP replay, computed keys, and Mule deferred execution without Spider.
- Added XML documentation generation and summary comments for public package APIs.
