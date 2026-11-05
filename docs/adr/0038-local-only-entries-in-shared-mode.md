# ADR-0038: Entries kept on this PC only while using a shared database

* Status: Accepted
* Date: 2026-11-05
* Requirements: FR-29, FR-04, FR-22, FR-24, FR-33, FR-50, NFR-10, NFR-11

## Context

In shared-database mode every entry is written to the shared database and seen by the whole team. Users also want a
few private entries — a lab VM, a personal jump host, credentials they must not share — without switching the whole
app back to local mode. They also want to move an entry from private to shared (and back) without retyping it and its
passwords.

## Options considered

1. **A flag column in the shared database** that hides an entry from others — the row and its ciphertext would still
   sit in the shared database, readable by every vault user and the DBA; rejected.
2. **Extra rows in the offline cache file** — the cache is per remote connection string and rebuilt from the shared
   database; mixing private rows into it complicates sync and they would vanish with a new cache.
3. **This PC's local-mode vault (`harbor.db`)** opened next to the shared database — chosen.

## Decision

* In remote mode `HarborWorkspace` also opens the local vault file used by local mode (`harbor.db`), with its own
  data-encryption key wrapped by DPAPI for this Windows user and machine (as in local mode, NFR-10). If it cannot be
  opened (e.g. copied from another user) only local-only entries are disabled; the shared database still opens.
* `ListAsync` returns the cached shared entries followed by the local vault's entries, the latter marked
  `ConnectionEntry.IsLocalOnly`. The flag is not a column; it says which store an entry came from. **No schema change.**
* Save, delete and password reveal route by that flag. Local-only entries can be edited while the shared database is
  offline (FR-22 keeps shared entries read-only).
* **Moving** (`MoveAsync`) needs the shared database online. The entry is copied to the target store under **new ids**
  (entry and accounts), its passwords decrypted with the source key and re-encrypted with the target key bound to the
  new ids (NFR-11), and then deleted from the source, which keeps a tombstone so other caches drop it (FR-23). New ids
  avoid clashing with the source's tombstone if an entry is moved back later. The copy is written first; if the delete
  fails (e.g. a stale version, FR-24), the copy is withdrawn, so an entry is never lost and never left in both places.
  Pins and recent entries (FR-50) follow the new id.
* No presence record is published for a local-only entry (FR-33): others do not know it exists.
* Switching to local mode shows the same local-only entries, because they are the local vault.

## Consequences

* Private entries never reach the shared database, its backups or the DBA.
* A moved entry has a new id; anything keyed by the old id outside the app (none today) would not follow it.
* Local-only entries are not backed up by the shared database; losing the PC or the Windows profile loses them.
