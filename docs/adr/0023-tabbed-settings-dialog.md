# ADR-0023: Settings dialog on tabs with compact, paired fields

* Status: Accepted
* Date: 2026-10-30
* Requirements: FR-26, FR-27, FR-45

## Context

The Settings dialog had grown into one long scrolling page (storage, database form, vault key protection, sync and
presence, sign-in, Remote Desktop, data folder, about). Users reported that it is too long, that every text box
spans the whole width even for a short value (a port-sized number, a user name), and that the scroll bar of the
built-in Fluent theme (ADR-0021) — which overlays the content instead of reserving space — covers the right edge
of the input fields.

## Options considered

1. **Keep one page, collapse sections into expanders** — less scrolling, but a user editing the database still
   opens and closes several expanders, and the overlay scroll bar problem stays.
2. **Navigation list on the left (Windows Settings style)** — scales to many pages, but costs horizontal space the
   paired fields need, and four pages do not justify it.
3. **A `TabControl` with four tabs**, each with its own scroll viewer and a right gutter; related fields on one row.

## Decision

Option 3.

* Tabs: **Storage** (local/shared choice, provider, connection form or raw string, create database, test
  connection), **Shared vault** (key protection, schema auto-upgrade, sync interval, presence timeout/heartbeat),
  **Sign-in**, **General** (Remote Desktop, data folder, about).
* The Shared vault tab stays visible with local storage but is disabled, with a hint pointing to the Storage tab,
  so its settings are discoverable and saved values are not hidden away.
* Paired fields share a grid row: server + database name, user name + password, Entra client ID + tenant. The three
  timing values sit side by side in a `WrapPanel` with narrow boxes; the anonymous name gets a fixed-width box.
* Each tab page has a 20 px right margin inside its `ScrollViewer`, so the overlay scroll bar never covers inputs.
* The error text and the Save/Cancel buttons stay below the tab control, so a validation error is visible whatever
  tab is open. Radio groups keep their own `GroupName` (FR-26).

## Consequences

* A validation error can refer to a field on another tab; the message names the field, so the user switches tab.
* New settings must go on the matching tab; `ViewSourceTests` checks the tab list and the scroll gutter.
* No view-model or settings-file change.
