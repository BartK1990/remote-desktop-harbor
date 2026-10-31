# ADR-0028: Quick connect palette, environment tags from folder names, personal pins and recents

* Status: Accepted
* Date: 2026-10-31
* Requirements: FR-48, FR-49, FR-50, NFR-01, NFR-03
* Builds on: ADR-0025, ADR-0026

## Context

Engineers usually know which machine they want and type part of its name or host. The search box filters the tree,
but connecting still takes Down, arrow keys and Enter, and an account other than the default one needs the entry
expanded first. Folders such as *Production* and *Staging* carry the most important context — touching production —
but look like any other folder. And each engineer works with a handful of machines out of hundreds.

## Options considered

**Quick connect.** (a) Make the search box smarter; (b) a command-palette overlay with its own ranked result list.
(b) keeps the tree search as it is (a filter) and gives one place that lists entries *and* accounts, ranked, with
Enter to connect.

**Environment tags.** (a) A per-folder setting stored in the shared database — exact, but needs a schema migration
for every provider, an editor for folders (which are not entities today) and a rule for renamed folders; (b) derive
the tag from the folder name — zero setup, works for every existing catalog and shared database immediately, but
only knows a fixed word list; (c) a per-entry field — duplicated across hundreds of entries.

**Pins and recents.** (a) In the shared database — visible to and editable by everyone; (b) in each user's
`settings.json` — personal, no schema change, works offline.

## Decision

* **Quick connect (b):** `Ctrl+K` opens an overlay in the main window (no second window, so it opens instantly).
  `QuickSearch.Rank` (Core, tested) scores candidates in memory — every query word must match; name over host over
  user over folder; pinned, then recent, first among equals; at most 12 results — which stays fast with 10 000
  entries (NFR-03). Enter selects the result in the tree (clearing a search or filter that hides it) and runs the
  normal Connect, so the takeover confirmation (FR-33) and presence apply unchanged. `Ctrl+F` keeps focusing the
  tree search; `Ctrl+K` no longer does.
* **Environment tags (b):** `EnvironmentTags.FromName` matches whole words of a folder name (`Production`,
  `Dev servers`, `QA-Team`, `PreProd` → STG, not PROD). A folder row shows the tag of its own name; an entry takes the
  outermost tagged folder above it. Tags use text plus colour tokens with a light and dark variant (4.5:1).
* **Pins and recents (b):** `PinnedEntryIds` and `RecentEntryIds` (last 10 launches, `RecentList.Push`) in
  `HarborSettings`. Filter chips All / In use / Pinned / Recent combine with the search.

## Consequences

* No schema change; nothing new is written to a shared database.
* A folder named outside the word list (e.g. *Kunde-Produktion*) gets no tag. A configurable mapping or per-folder
  tags (option a) can be added later without breaking the derived tags.
* Pins and recents do not follow a user to another computer (settings are per machine, like the collapsed folders).
* A second-level action menu in the palette (copy password, edit) is left out; Shift+Enter selects the entry so the
  normal shortcuts apply.
