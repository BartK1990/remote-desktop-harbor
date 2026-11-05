# ADR-0040: Smart groups: rule-based virtual groups in the entry tree, kept in personal settings

* Status: Accepted
* Date: 2026-11-05
* Requirements: FR-62, FR-02, FR-49, FR-50, NFR-03
* Builds on: ADR-0017, ADR-0028

## Context

An entry lives in exactly one folder, but engineers think about machines along several axes at once: *all SQL
servers*, *everything in production I reach as `svc-admin`*, *every host named `web-NN`*. Remote Desktop Connection
Manager (RDCMan), which many users come from, answers this with **smart groups**: a group in the tree that holds no
servers itself but lists every server whose properties match its rules (*Any* / *All*, property, operator, value),
and that is scoped to the group it is placed in. The search box and the Pinned filter (FR-50) cover one-off needs,
but are not saved and are only one at a time.

## Options considered

**What a smart group is.**
1. A saved search shown as another filter chip — little code, but only one at a time and not part of the tree.
2. A tree row that lists matching entries as references (RDCMan's model) — several groups visible side by side,
   placed in folders; needs reference rows that act as their entry.
3. Tags assigned to entries, with tag groups — needs a schema change and editing every entry.

**Where they are kept.**
1. In the shared database — one set for the team, like RDCMan's groups in a shared `.rdg` file; needs a new table
   and migration for every provider, offline editing rules and agreement on who may change them.
2. In each user's `settings.json` — personal, no schema change, works offline and with entries on this PC only
   (FR-29), like pins (ADR-0028).

## Decision

* **Tree rows (option 2).** `SmartGroupViewModel` rows list `EntryLinkViewModel` rows. A link has its own
  selection and container (the same `EntryViewModel` twice in one `TreeView` would share `IsSelected` and confuse
  the tree) and renders with the entry's own template. Selecting a link selects its entry for the details pane and
  every command; the entry keeps its row in its folder. Links have no account rows; Connect's account menu covers them.
* **Rules in Core.** `SmartGroup` / `SmartGroupRule` and `SmartGroups.Matches` (OS-neutral, tested) evaluate in
  memory: fields name, host, folder, user name (any account; negative operators hold only if no account matches),
  notes, environment tag (FR-49) and kind; operators contains / does not contain / is / is not / starts with / ends
  with / matches / does not match (regex, parsed once and cached, 50 ms timeout per value); *All* or *Any*. Case is
  ignored. A group in a folder is scoped to that folder and below, as RDCMan scopes a smart group to its parent group.
  An invalid pattern never matches and `Validate` blocks saving.
* **Placement.** In its folder before the sub-folders, or at the top. The search and filter chips apply inside groups;
  while searching, empty groups hide like folders. A group whose folder has no visible entries is shown at the top
  level when nothing is filtered (so it can still be found and edited) and hidden otherwise.
* **Personal storage (option 2).** `HarborSettings.SmartGroups` and `CollapsedSmartGroups` (ids). Groups match by
  rules, not entry ids, so moving an entry between vaults (new id, ADR-0038) needs no remapping.
* **Editing.** Tree context menu (**New smart group…** in the selected folder, **Edit…**, **Delete**), F2 / Del on a
  group row. The editor shows a live count of matching entries.

## Consequences

* No schema change; nothing is written to a shared database, and smart groups work offline.
* A team cannot share smart groups yet, and they do not follow a user to another computer. Storing them in the shared
  database (option 1) can be added later with the same `SmartGroup` model, e.g. as "team smart groups" next to
  personal ones.
* Rules are evaluated on every tree rebuild; with 10 000 entries this stays in memory and fast (NFR-03, tested).
* RDCMan's nesting of smart groups inside smart groups and its rules on server properties Harbor does not have
  (e.g. gateway settings) are left out.
