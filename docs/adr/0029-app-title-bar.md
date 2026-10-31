# ADR-0029: The app draws its own narrow title bar; production entries ask before connecting

* Status: Accepted
* Date: 2026-10-31
* Requirements: FR-47, FR-48, FR-51, FR-52
* Builds on: ADR-0021, ADR-0025, ADR-0026, ADR-0028

## Context

The built-in WPF Fluent theme keeps the system title bar: a separate strip that does not follow the app's colours
and holds nothing but the title. The main toolbar, meanwhile, ran out of width (quick connect, New, Import, Refresh,
the signed-in user and Settings needed about 1 060 px, more than the 720 px minimum window width). Separately, the
redesign asked for a guard against opening a production server by accident.

## Options considered

1. **Keep the system title bar** — free, but it cannot hold app content and ignores the app theme.
2. **WPF UI's `FluentWindow`/`TitleBar`** — rejected with ADR-0021 (third-party dependency for a similar look).
3. **`WindowChrome` + an own `TitleBar` control** — built into WPF; the system still handles dragging, double-click,
   snapping, resizing, shadow and the system menu; the app only draws the 32 px bar and three caption buttons.

## Decision

Option 3.

* Each window sets `WindowChrome` (`CaptionHeight=32`, `ResizeBorderThickness=6`, `GlassFrameThickness=-1` to keep
  the system shadow, rounded corners and any Fluent backdrop, `UseAeroCaptionButtons=False`) and puts
  `Views/TitleBar` in its top row.
* `TitleBar`: icon, the window title (dimmed while inactive), a centre and a right slot that stay clickable
  (`WindowChrome.IsHitTestVisibleInChrome`), and Minimize / Maximize-Restore / Close buttons (46 × 32, Segoe Fluent
  Icons; Close red on hover). Dialogs show Close only. When maximized it adds the resize border to the content
  margin, because a custom-frame window extends past the screen by that much.
* The main window moves Quick connect (a compact search-like field, min 140, max 340 px) and the signed-in user into
  the title bar; the toolbar keeps the search box, New, Import .rdp…, Refresh and Settings. The quick connect
  palette opens below the title bar, so the caption buttons stay usable.
* Production guard (FR-51): connecting to an entry tagged PROD (FR-49) asks first; a takeover question (FR-33)
  mentions production instead of asking twice. `ConfirmProductionConnect` (default on) in Settings → Remote Desktop.

## Consequences

* The toolbar now fits at the 720 px minimum width.
* Windows 11 snap layouts: Windows shows the layout flyout only over a part that hit tests as `HTMAXBUTTON`. The
  title bar hooks the window procedure: `WM_NCHITTEST` over the Maximize button returns `HTMAXBUTTON`; the button's
  hover and pressed look then come from `WM_NCHITTEST` / `WM_NCMOUSELEAVE` (tracked with `TrackMouseEvent`) and its
  click from `WM_NCLBUTTONUP` (`WM_NCLBUTTONDOWN` is swallowed so Windows draws no classic button). Win32 bits live
  in `Harbor.Windows.Interop.WindowInterop`.
* Message boxes got the same title bar later (ADR-0030).
