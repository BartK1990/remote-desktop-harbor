# ADR-0010: Session presence — heartbeat records with server-clock TTL

* Status: Accepted
* Date: 2026-10-29
* Requirements: FR-30..FR-36

## Context

Team members must see who currently has an RDP session open through the app, with name and e-mail. Apps crash,
laptops sleep and networks drop, so "start/stop" records alone would leave ghosts.

## Decision

* Table `ActiveSessions`; one row per launched session (`SessionPresenceService` / `PresenceLease`).
* **Lease semantics**: on launch insert with `ExpiresUtcMs = now + TTL` (default 120 s); a background heartbeat every
  45 s (≤ TTL/2) extends it; process exit or app close deletes it.
* **Readers filter `ExpiresUtcMs > now`**; expired rows are never shown and are purged after one day.
* **All times come from the database clock** (`provider.NowUnixMs` in SQL), so client clock skew cannot make records
  look alive or dead.
* Presence is best-effort: failures are logged, never block launching; if the DB was down at launch, the lease keeps
  retrying and publishes once it is back; if a row vanished, the heartbeat re-creates it.
* Connecting to an in-use entry asks for confirmation (mstsc would otherwise kick the other user on single-session hosts).

## Consequences

* After a crash, the "in use" badge lingers for at most one TTL.
* Only sessions launched by this app are visible (not ones opened directly with mstsc).
* Polling interval for other clients' view: 20 s.
