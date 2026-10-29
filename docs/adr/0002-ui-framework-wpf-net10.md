# ADR-0002: UI framework — WPF on .NET 10

* Status: Accepted, amended by ADR-0020 (WPF UI library instead of the built-in Fluent theme)
* Date: 2026-10-29
* Requirements: NFR-01, NFR-02, NFR-03, NFR-05

## Context

We need a fast, native-looking Windows desktop UI that runs unpackaged without installation or admin rights,
and can later be published to the Microsoft Store.

## Options considered

1. **WinUI 3 (Windows App SDK)** — modern Fluent look. Unpackaged use needs either the Windows App SDK runtime
   installed (MSI, typically admin) or self-contained mode, which adds ~40 MB and is slower to start than WPF in
   practice. XAML compiler only runs on Windows, so builds can't be validated on Linux agents. Tooling still maturing.
2. **WPF on .NET 10** — mature, very fast for list/detail apps, hardware-accelerated, full UI virtualization.
   .NET 9+ ships a Fluent theme (`ThemeMode="System"`, light/dark). Runs self-contained without installation.
   Builds on any OS with `EnableWindowsTargeting`. Store-publishable as MSIX or as Win32 app.
3. **Avalonia** — cross-platform, supports Native AOT (fastest cold start), but non-native look, extra
   dependency, and cross-platform is not a requirement.
4. **WinForms** — fastest startup, but dated UI and weaker data binding.

## Decision

WPF on .NET 10 with the built-in Fluent theme, MVVM via CommunityToolkit.Mvvm (source generators, no reflection),
no DI container (hand-written composition root in `AppHost`) to keep startup lean.

Startup rules (NFR-01): show the window immediately; sign-in, database open and sync run off the UI thread;
the list renders from the local SQLite file before any network access; lists are virtualized (also when grouped).

## Consequences

* Native AOT and trimming are not available for WPF; we use ReadyToRun + tiered PGO instead (ADR-0003).
* Business logic lives in OS-neutral libraries (`Harbor.Core`, `Harbor.Data`) so it can be tested on Linux CI
  and reused if the UI is ever replaced (e.g. by WinUI 3).
* `ThemeMode` is flagged experimental (WPF0001); warning suppressed deliberately.
