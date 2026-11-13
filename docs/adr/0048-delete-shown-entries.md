# ADR-0048: Delete the entries the tree shows instead of multi-selection

* Status: Accepted
* Date: 2026-11-13
* Requirements: FR-69, FR-50, FR-29, FR-22, FR-23, FR-24, FR-53

## Context

Entries can only be deleted one at a time. Users who switched from local mode to a shared database and put the same list
there end up with every entry twice — once shared, once kept on this PC (ADR-0038) — often more than a hundred. Deleting
those one by one is not practical. The WPF `TreeView` has no multi-selection: the tree, its keyboard handling, the
details pane and every command work on one selected row.

## Options considered

1. **Multi-selection in the tree** (Ctrl/Shift+click, check boxes) — familiar, but `TreeView` needs a custom selection
   model that works with virtualization and recycled rows, folders, account rows and smart group references; every
   command would have to define what it does for several rows. Large change for one use.
2. **Delete a folder with its entries** — helps only when the entries to delete share a folder; duplicates do not.
3. **Delete every entry the tree shows** (after search and filter) — chosen. The existing search, filters (including
   This PC, FR-50) and smart groups already describe the set; one action deletes it.

## Decision

* **Delete shown entries…** in the tree context menu deletes the entries that pass the current search and filter, each
  once (smart group references are not counted again).
* It is **enabled only while the list is narrowed** — a search or a filter other than All — and shows something, so a
  single action never deletes the whole catalog.
* One confirmation (FR-53, Cancel by default) gives the counts: in total, removed for everyone in the shared database,
  removed from this PC, and shared entries kept because the shared database is offline.
* `HarborWorkspace.DeleteManyAsync` deletes the entries one by one through the same path as a single delete (tombstones
  for shared entries, FR-23). An entry that cannot be changed now is skipped, a stale version or a lost connection fails
  only that entry, and the others are still deleted; the result lists what was deleted and what failed and why.
  Partial success is acceptable because each delete stands on its own and the app reports what remains.
* No schema change.

## Consequences

* With the This PC filter, all duplicates kept on this PC go in one action and never touch the shared database.
* Large deletes in a shared database run one statement per entry; fine for hundreds, not tuned for many thousands.
* Users who really want to delete the whole catalog must filter first (e.g. search) — deliberate friction.
* Multi-selection stays possible later; this action would then act on the selection.
