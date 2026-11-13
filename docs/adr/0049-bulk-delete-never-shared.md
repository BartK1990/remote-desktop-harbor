# ADR-0049: Deleting the shown entries never deletes from the shared database

* Status: Accepted
* Date: 2026-11-13
* Requirements: FR-69, FR-29, FR-23

## Context

ADR-0048 added **Delete shown entries…**, which deleted every entry the tree shows, shared ones included, after one
confirmation. In a shared database that is one search and two clicks away from deleting much of the team's catalog for
everyone. Tombstones (FR-23) make the rows disappear from every client; there is no undo in the app. The action was
added for a local problem — duplicates kept on this PC — and that problem never needs shared entries deleted in bulk.

## Options considered

1. **Keep it, rely on the confirmation** — the counts are shown, but a confirmation is easily clicked through.
2. **Extra friction for shared entries** (type the count, a setting) — more UI for a case nobody asked for.
3. **Never delete shared entries in bulk** — chosen.

## Decision

* In shared mode **Delete shown entries…** deletes only the shown entries kept on this PC (FR-29); shown shared entries
  are kept and the confirmation says how many. It is disabled when no shown entry is kept on this PC. In local mode
  nothing is shared and it deletes every shown entry, as before.
* `HarborWorkspace.DeleteManyAsync` enforces the rule itself: given an entry in the shared database it throws
  `InvalidOperationException` before deleting anything, so no future caller can bulk-delete shared entries.
* Shared entries are deleted one at a time, as before ADR-0048.

## Consequences

* A slip can no longer remove many entries for the whole team; at worst it removes entries on this PC only.
* Cleaning up many shared entries stays slow on purpose; a DBA can still do it in the database if ever needed.
* Bulk deletes no longer depend on the shared database being online.
