# ADR-0045: Connections can open in Harbor's resizable Remote Desktop window

* Status: Accepted
* Date: 2026-11-11
* Requirements: FR-67, FR-10, FR-33, FR-47, NFR-12
* Amends: ADR-0008 (mstsc stays the default, not the only client), ADR-0042 (its window code is shared)

## Context

Users want a Remote Desktop window they can maximize like any other window — not full screen, so the taskbar and the
other monitors' windows stay usable — with the remote resolution following the window. mstsc can do this only with the
right `.rdp` settings (windowed, dynamic resolution) and its maximize button switches to full screen when the window
covers a whole screen. The transfer window (ADR-0042) already hosts the Remote Desktop ActiveX control and behaves this
way for its own size.

## Options considered

1. **Reuse the ActiveX control in a second window of the app for ordinary connections**, resizing the session with
   `UpdateSessionDisplaySettings` (the control's dynamic resolution) when the window changes size. The control ships
   with Windows; the app owns the window, so maximize is a plain maximize.
2. **Launch mstsc windowed with `dynamic resolution:i:1`** for this choice. No new code, but mstsc's maximize still
   goes full screen on a single screen and its window cannot be told apart from full screen per launch.
3. **Embed all connections in tabs of the main window.** Far larger: focus, keyboard hooks and several sessions in one
   window, and it changes the app's model of one window per connection.

## Decision

Option 1, as a choice: mstsc stays the default (FR-10).

* `HarborSettings.ConnectInHarborWindow` (Settings → Remote Desktop) makes Connect open Harbor's window. A second command
  connects the other way once (Shift+Enter, tree and More menus), so both are always at hand.
* `RdpWindowSettings.For(entry)` (Harbor.Core, tested) maps the entry's `.rdp` settings through `RdpOptions` to what the
  control supports; full screen and multiple monitors are ignored, a drive selection is not shared (the control shares
  drives all or nothing), gateway entries return `null` and open in mstsc, like transfers.
* `RdpWindowHost` (Harbor.Windows) runs the window on its own STA thread, as ADR-0044 requires for the transfer window.
  The control code both windows use (class lookup, thread, events, settings helpers) moved to `RdpControl`.
* The desktop starts at the window's size (or the entry's fixed size with dynamic resolution off). After sign-in, a
  size change is applied 400 ms after the last resize event with an even width within 200–8192 px and this screen's
  scale; failures right after sign-in are retried a few times, after that (old server or control) the picture is
  smart-sized and the next connection starts at the window's size.
* The password is set as `ClearTextPassword` on the control for each attempt, in memory only (NFR-12); nothing is
  written to Credential Manager, so `KeepCredentialAfterLaunch` does not apply to this window.
* The window's session completes when it closes, so presence and the recent list work as for mstsc (FR-33).

## Consequences

* A maximized connection without full screen, with the resolution matching the window, in one setting or one shortcut.
* The window does not offer mstsc's connection bar, multi-monitor, a drive selection or Remote Desktop Gateway; those
  entries or needs use mstsc (the menu item names it).
* The window and resizing only run on Windows and are tested by hand; the settings mapping and size limits are covered
  by `RdpWindowTests` (NFR-30).
