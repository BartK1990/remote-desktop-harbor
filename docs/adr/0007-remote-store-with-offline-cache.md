# ADR-0007: Remote store as source of truth with local read-only offline cache

* Status: Accepted
* Date: 2026-10-29
* Requirements: FR-21..FR-24, NFR-01

## Context

With a shared database, all data must be stored there, but the app must keep working (at least for connecting)
when the database is unreachable (VPN down, travel). Startup must not wait for the network.

## Decision

* `HarborWorkspace` wraps two `IConnectionStore`s: the **remote** (source of truth) and a **local SQLite cache**
  with the same schema, one cache file per remote connection string.
* **Reads** always come from the cache (fast startup, offline). **Writes** go to the remote first, then the stored
  row (with server-assigned version/timestamps) is applied to the cache.
* **Sync** is an incremental pull: rows with `UpdatedUtcMs > watermark − 5 min` (overlap absorbs commit-order skew);
  applying is idempotent; deletes are tombstones so they propagate.
* **Offline** (any connectivity failure): read, search, copy and launch from the cache; writes are rejected with a
  clear message; the app retries on every sync tick and switches back automatically.
* **Conflicts**: optimistic concurrency on `Version`; stale writes are rejected, the user reloads and retries.

## Consequences

* No offline edit queue (avoids merge conflicts on secrets) — see SPEC Q2.
* The cache contains only ciphertext plus the locally wrapped DEK; losing the laptop does not expose passwords.
* Very large catalogs pull fully on first start; afterwards only deltas.
