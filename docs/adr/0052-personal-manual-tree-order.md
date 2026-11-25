# ADR-0052: The manual order of the entry tree is personal and kept in the settings

* Status: Accepted
* Date: 2026-11-24
* Requirements: FR-72, FR-02, FR-50

## Context

The entry tree was always sorted by name. Users want to put entries and folders in their own order (e.g. the
servers they use most first), as an option next to the name order. The order has to be stored somewhere and has to
work in local mode, with a shared database and while it is offline.

## Options considered

1. **A sort column on entries in the database** — one order for the whole team. Needs a migration for every
   provider, sync of the column, and a folder table (folders only exist as entry paths); one user's reordering would
   change everybody's tree, and reordering would be impossible while offline.
2. **A personal list in the user's settings** (`ManualOrder`), like pins and smart groups (ADR-0028, ADR-0040) — no
   schema change, works offline and in every mode, nobody else's tree changes.

## Decision

Option 2. `HarborSettings.TreeOrder` chooses **By name** (default) or **Manual**; `HarborSettings.ManualOrder` is one
flat list of keys — entry ids and `folder:<path>` — where an earlier key comes first among its siblings. Only siblings
are ever compared (sub-folders with sub-folders, entries with the entries of their folder), so the flat list serves
the whole tree. Keys not in the list follow the listed ones by name, so new entries need no bookkeeping. Moving a row
lists all its siblings in their shown order and drops keys of entries and folders that no longer exist. Smart groups
stay first and by name; folders stay before entries.

Moving is offered only while the whole tree is shown (no search, filter All), so the neighbours a row passes are its
real siblings: by keyboard (Alt+Up / Alt+Down), the context menu, arrow buttons above the tree, and drag and drop among
siblings. Dragging never moves an entry to another folder, which would change the entry itself (shared with the team).

## Consequences

* No migration, nothing in the shared database; teammates keep their own order.
* The order is not part of a backup: restored entries get new ids (FR-68), so their keys would not match anyway.
* Renaming a folder, or moving an entry to another folder, loses its place: it follows the moved rows by name.
* **Reset manual order…** empties the list, which is all it takes to get back to name order.
