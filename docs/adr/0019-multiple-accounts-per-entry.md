# ADR-0019: Several user accounts per entry

* Status: Accepted
* Date: 2026-10-29
* Requirements: FR-01, FR-04, FR-09, FR-11, FR-30, FR-33, NFR-11

## Context

An entry held exactly one user name and password. Windows Server hosts allow several Remote Desktop sessions at once
(two administrative sessions, more with RDS), and teams often keep several accounts for one server — a personal admin,
a service account, a break-glass account. Users had to duplicate the whole entry (host, `.rdp` settings, notes) for
each account. They asked for one entry with several users configured underneath it.

Constraints: shared databases are used by clients of different versions at the same time; passwords must stay
encrypted per row (ADR-0005); sync, tombstones and optimistic concurrency work per entry (ADR-0007); SQL must stay
portable (ADR-0004).

## Options considered

1. **Duplicate entries** (status quo) — no change; host and settings drift apart between copies, and presence cannot
   tell that two entries are the same server.
2. **All accounts in a JSON column on `Connections`** — one row, so sync and concurrency come for free; but secrets
   are hidden inside a text blob (base64 ciphertext), the column is opaque to SQL, and the default account would
   either move into the blob (breaking older clients) or be duplicated.
3. **A `ConnectionAccounts` child table for the additional accounts; the entry's existing `Domain`/`UserName`/
   `PasswordCipher` stay the default account** — relational and portable; no data migration; older clients keep
   reading and editing the default account. Child rows must be written and synced together with their entry.

## Decision

Option 3.

* `ConnectionAccounts (Id, ConnectionId, Position, Domain, UserName, PasswordCipher)` is added by migration 2 in every
  provider. An empty `Domain` means "the entry's domain".
* The store writes an entry's accounts in the same transaction as the entry (delete + insert), so the entry's
  `Version` and `UpdatedUtcMs` cover them: optimistic concurrency, `ListChangedSince` and `ApplyReplica` treat an entry
  and its accounts as one unit. Deleting an entry deletes its account rows (and so their secrets); a tombstone never
  carries accounts, even if an older client left rows behind.
* Each account's password is its own AES-GCM envelope with the **account id** as associated data, so a ciphertext
  moved to another account or to the entry fails to decrypt (NFR-11).
* `ActiveSessions` gains `AccountName` (the qualified user name connected as). Connecting asks for confirmation only
  when a live session uses the same account, or its account is unknown (records from older clients) — another account
  runs side by side on a multi-session host.
* UI: an entry with additional accounts gets one child row per account (default first) in the tree; Connect, Copy user
  and Copy password act on the selected account row, or on the default account when the entry row is selected.
  A single-account entry stays a single row (ADR-0017).
* Accounts of one host share the `TERMSRV/<host>` credential (ADR-0008). The launcher remembers which launch wrote
  the pending credential per target: an older launch's cleanup leaves a newer one's credential alone, and a launch
  without a password removes a credential written moments before for another account.

## Consequences

* No data migration: existing entries are single-account entries. Older clients see and can edit only the default
  account; their edits keep the additional accounts (they do not touch the child table).
* Clients without DDL rights need the schema upgraded (migration 2) before they can read the shared database, as with
  any migration (FR-25).
* Reads of entries and accounts are two statements; a cache may briefly hold an entry with accounts from a newer
  version, which the next sync corrects (the watermark overlap re-reads it). A stale edit is still rejected by `Version`.
* With "keep the credential" on, a kept credential of one account is reused when another account of the same host
  without a stored password is launched; store a password for every account, or turn the setting off.
