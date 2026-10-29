# ADR-0020: Fluent look with WPF UI (lepoco) instead of the built-in WPF Fluent theme

* Status: Superseded by ADR-0021 (reverted; the built-in Fluent theme is kept)
* Date: 2026-10-29
* Requirements: FR-08, NFR-01, NFR-02, NFR-05
* Amends: ADR-0002

## Context

ADR-0002 chose WPF with the Fluent theme built into .NET 9+ (`ThemeMode`). That theme is still flagged experimental
(WPF0001), has no Fluent title bar or Mica backdrop, no Fluent message box, and only restyles the stock controls:
no icon buttons, no search box with placeholder and icon, no password reveal button, no info bar. The app had
started to hand-roll these (font-glyph icons, custom badges, accent colours by hex value).

## Options considered

1. **Keep the built-in Fluent theme** — no dependency; but experimental, limited control set, keeps the hand-rolled parts.
2. **WPF UI (`WPF-UI` by lepoco, MIT)** — mature Fluent library for WPF: `FluentWindow` with Mica and a Fluent
   `TitleBar`, theme dictionaries with the WinUI resource keys, `ApplicationThemeManager` / `SystemThemeWatcher`
   for light/dark/system, Fluent `MessageBox`, `Button` with `SymbolIcon`, `TextBox` with placeholder/icon,
   `PasswordBox` with reveal, `InfoBar`. Plain WPF assembly: runs self-contained, no install, Store-compatible.
   Adds two assemblies (~ a few MB) and parses its control dictionaries at startup.
3. **WinUI 3** — rejected in ADR-0002 for the same reasons as then.

## Decision

Use WPF UI (version pinned in `Directory.Packages.props`). Every window is a `ui:FluentWindow` with
`ExtendsContentIntoTitleBar` and a `ui:TitleBar`, Mica backdrop. `App.xaml` merges `ui:ThemesDictionary` and
`ui:ControlsDictionary`; the built-in `ThemeMode` is no longer used. Colours come from the WPF UI theme resources
(`TextFillColor*`, `SystemFillColor*`, `AccentFillColorDefaultBrush`, …), never hard-coded.

`ThemeService` (SPEC FR-08) maps *Light*/*Dark* to `ApplicationThemeManager.Apply` and *System* to
`ApplicationThemeManager.ApplySystemTheme`; while *System* is chosen the main window is registered with
`SystemThemeWatcher` so a Windows light/dark switch applies live.

Confirmations and errors use the WPF UI `MessageBox`, which is asynchronous, so `IDialogService.ConfirmAsync` /
`ShowErrorAsync` return tasks. Closing the main window with open sessions cancels the close, awaits the answer and
closes again (the ADR-0002 close-after-dispose pattern is kept).

## Consequences

* The WPF0001 suppression is gone; the UI no longer depends on an experimental API.
* Native dialogs (open file) stay Win32; entry editor and settings stay modal `FluentWindow`s rather than in-window
  `ContentDialog`s, so the view models are unchanged apart from the async message boxes.
* Startup cost grows slightly by loading the WPF UI dictionaries; still within NFR-01, to be watched.
* The tree row template and the selectable read-only text style are still our own (WPF UI has no compact tree row).
