# ADR-0034: Entry list size: zoom the tree rows, with a reset to 100 %

* Status: Accepted
* Date: 2026-11-04
* Requirements: FR-55, FR-02, FR-47

## Context

The entry tree uses compact one-line rows (ADR-0017). On large or high-resolution screens, or for users who prefer
larger text, the rows are small; on a small laptop screen some users want them even denser. Windows display scaling
changes the whole app, not just the list. Users asked for a way to resize the entries and to get back to the default
easily.

## Options considered

1. **A setting in Settings (Small / Normal / Large)** — simple, but slow to try out and hidden from where it matters.
2. **Scale the whole `TreeView` with a `LayoutTransform`** — one line, but the scroll bar and the card padding grow too,
   and the Fluent overlay scroll bar becomes oversized.
3. **Zoom each row (and the indent) in the item template, with buttons above the tree and the usual zoom keys** —
   the content grows, the scroll bar keeps its size; familiar from browsers (Ctrl+Plus / Minus / 0, Ctrl+wheel).

## Decision

Option 3. The level runs from 80 % to 160 % in 10 % steps (`Core/Model/TreeZoom`, OS-neutral and unit tested) and is
stored as `TreeZoom` in the personal settings. A `ScaleTransform` bound to the level is the `LayoutTransform` of each
row's header grid and of the 16 px indent before its children, so nested rows stay aligned at every level. Above the
tree, next to the filter chips, are **−**, the current level and **+**; clicking the level (or Ctrl+0) resets to
100 %, and is disabled at 100 %.

## Consequences

* Text is rendered at the scaled size by WPF's layout transform, so it stays sharp.
* The size applies to the entry tree only; the details pane and Quick connect keep their size.
* Ctrl+Plus / Ctrl+Minus / Ctrl+0 are taken by the main window for this.
