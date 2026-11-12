# ADR-0047: The app always lists its own sessions as active

* Status: Accepted
* Date: 2026-11-12
* Requirements: FR-37, FR-29, FR-30..36, FR-22

## Context

Active sessions (FR-30..36, ADR-0010) were only known through the shared database. In local mode the presence
service had no store, so a launched connection never showed as in use; the same held for entries kept on this PC
only (FR-29), which were not even tracked, and while the shared database was offline the app showed no sessions at
all. Users working alone still want to see which connections they have open, and the takeover and close
confirmations should work for them too.

## Options considered

1. Write presence records to the local SQLite vault in local mode — reuses the store, but needs the database clock,
   heartbeats and purging for data only this process reads, and would leave stale rows after a crash.
2. Keep the app's own open sessions in memory in `SessionPresenceService` and merge them into the listed sessions —
   no schema change, nothing persisted, gone when the app ends (as the sessions are no longer tracked then).

## Decision

Option 2. Every launch takes a `PresenceLease`; the service keeps the open leases. A lease publishes to the shared
database only when one is used and the entry is shared (`BeginAsync(..., publish)`). `ListActiveAsync` returns the
shared records (unless offline) plus the own leases whose session id is not among them, so a published session is
listed once. Own sessions never expire while their lease is open.

## Consequences

* Local mode, local-only entries and offline mode show in-use markers, the session table and the takeover and
  close confirmations for this app's sessions.
* Nothing new is persisted and nothing local-only reaches the shared database (FR-29 stays private).
* Sessions started by another instance of the app on the same PC are not visible in local mode.
