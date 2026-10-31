# ADR-0026: Main window laid out for keyboard-driven engineers

* Status: Accepted
* Date: 2026-10-31
* Requirements: FR-06, FR-09, FR-33, FR-47
* Builds on: ADR-0025

## Context

The main window had a text-only toolbar, a details pane that listed every field as a label above its value and a
one-line status bar. Engineers connecting to many servers had to click several times to copy a host, could not see
how a connection would open (resolution, redirects, `/admin`) without the editor, had to expand an entry to pick
another account, and could not tell from the presence box whether connecting would take a session over. Only a few
actions had shortcuts, and none were shown in the window.

## Options considered

1. **Keep the layout, add shortcuts** — least change; the details pane stays long and the gaps above remain.
2. **Command bar + ribbon-like grouped actions** — familiar from Office; too heavy for five actions.
3. **Compact toolbar, details as cards, shortcuts shown next to actions** — the layout of the approved redesign
   proposal: everything an engineer reads or copies is one glance or one key away.

## Decision

Option 3, built from the ADR-0025 tokens and the stock Fluent controls.

* **Toolbar:** search box with icon, placeholder and its shortcut; icon + label buttons (New, Import .rdp…, Refresh
  with `F5`); the theme toggles (FR-08) stay; on the right the signed-in user and an icon-only Settings button.
* **Details pane:** folder and kind above the name; the host (with non-default port) in monospace accent colour
  below it. **Connect as &lt;account&gt;** names the account it uses; for an entry with several accounts an arrow
  next to it lists them, and picking one selects that account row and connects. Copy password and Copy user (with a copy icon) name their
  shortcuts in the tooltip, Edit shows `F2` on the button — short hints on the button, long ones in the tooltip, so
  the row fits the default window width; Copy host and Delete sit in a "More" menu. An active-sessions table (user, account,
  machine, since) says whether connecting with the selected account takes a session over or runs side by side (the
  same rule as the connect confirmation, `ActiveSessionExtensions.TakenOverBy`). Cards follow: **Connection** (host,
  port, user, password — each copyable with a button), **RDP profile** (a summary of the `.rdp` settings,
  `RdpSummary`; hidden for credentials only) and **Notes** (only when there are notes); then the last change and the
  entry version.
* **Status bar:** last status message, the number of entries in use, and the storage state with a coloured dot
  (online / offline) for a shared database.
* **Shortcuts:** `Ctrl+K` (as `Ctrl+F`) focuses search, `F2` edits, `Del` in the tree deletes after confirmation,
  `Ctrl+Shift+U` copies the user, `Ctrl+Shift+H` copies the host. A view-source test checks that every shortcut shown
  in the window is bound.

## Consequences

* Copy host is new (FR-06): it copies `host` or `host:port`, the form `mstsc /v:` and most tools accept.
* `Ctrl+K` is reserved for a future quick-connect palette; until then it focuses the search box.
* Delete is one click further away (More menu or `Del`), which makes it harder to hit by accident.
* Moving the theme toggles to Settings, proposed with the redesign, is not part of this change (FR-08 unchanged).
