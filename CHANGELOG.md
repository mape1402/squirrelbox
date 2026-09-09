# Changelog

All notable changes to SquirrelBox will be documented in this file.

The format is based on Keep a Changelog, and this project adheres to Semantic Versioning.

## [v1.0.0] - Unreleased

### Added

- Initial SquirrelBox solution skeleton.
- Added the core inbox/idempotency package with `IInboxService`.
- Added in-memory, Entity Framework Core, ASP.NET Core, Messaging, and TurtlePath package shells.
- Added ULID-based inbox entry identity.
- Added ambient inbox context support for open-or-continue flows.
- Added effective idempotency keys with explicit and computed-from-payload sources.
- Added payload conflict detection, payload verification, completion snapshots, failure details, expiration, and execution mode modeling.
- Renamed the messaging shell from `SquirrelBox.Pigeon` to `SquirrelBox.Messaging`.
