# ADR-0017: Compact folder tree for the entry list

* Status: Accepted
* Date: 2026-10-29
* Requirements: FR-02, FR-33, NFR-03

## Context

The entry list was a grouped `ListBox`: a header per folder and two-line rows (name + user @ host). With many
entries it needed a lot of scrolling, folders could not be collapsed, and there was no way to nest folders.
Users asked for a compact tree of their RDP and credential entries with collapsible folders and live filtering.

## Options considered

1. Keep the grouped `ListBox`, put an `Expander` in each group header — little code, but only one folder level and
   group headers do not take part in keyboard navigation.
2. A `TreeView` over a view-model tree built from the folder paths — nested folders, built-in keyboard navigation
   (arrows, ←/→ to collapse/expand) and hierarchical virtualization; selection has to be synced by hand because
   `TreeView.SelectedItem` is read-only.

## Decision

Option 2. The `Folder` column keeps a single string; `/` or `\` separates levels, so no schema change is needed and
existing flat folders stay as they are. Levels are matched case-insensitively. `FolderTree` (Harbor.Core, OS-neutral,
unit tested) builds the tree: folders first, then entries, both sorted by name. Rows are one line, about 22 px,
with only the name, a count on folders and the in-use badge; host and user are in the tooltip and the details pane.

Rows are reused across rebuilds and updated item by item, so syncs and filtering keep expansion, selection and
scroll position; collections with large changes that do not hold the selection are reset in one notification to
keep filtering 10 000 entries fast. While searching, every folder with matches is expanded; collapse state while
searching is kept apart from the normal one, which is saved in `settings.json` (`CollapsedFolders`).

## Consequences

* Users can nest folders without a migration; a folder name can no longer contain `/` or `\`.
* Entries without a folder are shown at the root after the folders instead of under “(No folder)”.
* Selection is kept in the view model (`IsSelected` on each row) and pushed to the `TreeView`.
