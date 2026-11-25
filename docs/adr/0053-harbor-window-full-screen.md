# ADR-0053: Harbor's Remote Desktop window goes full screen with the control's own full screen

* Status: Accepted
* Date: 2026-11-25
* Requirements: FR-74, FR-67

## Context

Harbor's Remote Desktop window (FR-67, ADR-0045) is always a window, so it can be maximized without covering the
taskbar. Users also want to switch it to full screen and back easily, as in mstsc.

## Options considered

1. **The window goes borderless over the screen** (the app handles full screen, `ContainerHandledFullScreen`) — full
   control over the look, but Harbor would need its own bar to leave full screen, shown when the mouse reaches the top
   of the screen over an ActiveX window that takes the mouse, and its own handling of Ctrl+Alt+Break.
2. **The control's own full screen** (`FullScreen = true`, `ContainerHandledFullScreen = 0`) — the same full screen
   as mstsc, which is built on this control: the familiar connection bar with pin, minimize, restore and close, and
   Ctrl+Alt+Break both ways; the control reports entering and leaving with events.

## Decision

Option 2. The window menu (Alt+Space or a right-click on the title bar) gets **Full screen**, enabled once signed in,
and Ctrl+Alt+Break works as in mstsc. On the control's enter / leave events the window resizes the session to the
screen it is on, or back to the window, through the same resize path as FR-67 (with its retries and smart sizing
fallback). The entry's own *full screen* display setting stays ignored: the window still opens as a window.

## Consequences

* Very little code; the bar and keys behave exactly as users know them from mstsc.
* The window has no button of its own for full screen: it is in the window menu and on Ctrl+Alt+Break.
* Full screen uses one screen; spanning all monitors stays a mstsc feature.
