# ADR-0021: Keep the built-in WPF Fluent theme (WPF UI reverted)

* Status: Accepted
* Date: 2026-10-29
* Requirements: FR-08, NFR-01, NFR-02
* Supersedes: ADR-0020

## Context

ADR-0020 moved the windows to WPF UI (lepoco) and shipped in 0.4.0. In practice the app looks almost the same as
with the Fluent theme built into WPF (`ThemeMode`), so the extra dependency, the larger startup work and the async
message-box plumbing it needed buy too little.

## Options considered

1. **Keep WPF UI** — Fluent title bar, Mica and icon buttons; but a third-party dependency for a nearly identical look.
2. **Return to the built-in Fluent theme** — no dependency, simpler code (synchronous message boxes), the look users
   already had before 0.4.0.

## Decision

Return to the built-in WPF Fluent theme as decided in ADR-0002. The WPF UI change is reverted in full, including the
re-rendered documentation mockups.

## Consequences

* `WPF-UI` is removed from `Directory.Packages.props`; `ThemeMode` (and its WPF0001 suppression) is back.
* `IDialogService.Confirm` / `ShowError` are synchronous again.
* The next release after 0.4.0 goes back to the pre-0.4.0 look.
