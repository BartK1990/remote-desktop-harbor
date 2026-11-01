# WPF UI: theme, tokens, title bar, dialogs, keyboard, tests

## Theme: built-in Fluent + own tokens

- `Application.ThemeMode` = `System` (default) / `Light` / `Dark`; suppress `WPF0001`. Apply it **before the first
  window is created**.
- Neutrals, accent and control colours come from the Fluent keys (`TextFillColorPrimaryBrush`,
  `CardBackgroundFillColorDefaultBrush`, `AccentFillColorDefaultBrush`, …). Don't build a full own palette — it
  fights the Windows accent and backgrounds.
- Add only what Fluent lacks:
  - `Themes/Tokens.xaml` — theme-independent: fonts (monospace `Cascadia Mono, Consolas` for anything a user would
    type in a terminal: hosts, ports, user names, paths; icons `Segoe Fluent Icons, Segoe MDL2 Assets`), sizes,
    shared text styles (`App.Text.Mono`, `App.Text.Error`, …).
  - `Themes/Colors.Light.xaml` / `Themes/Colors.Dark.xaml` — the **same** `App.*Brush` keys (error, success,
    warning, status, badge…), text ≥ 4.5:1 and marks ≥ 3:1 contrast on their theme's background.
- `ThemeService`: sets `ThemeMode` and swaps the colour dictionary; for *System* read
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` and listen to
  `SystemEvents.UserPreferenceChanged` to swap live. Views use colour tokens through **`DynamicResource`**.
- Styles `BasedOn` an implicit Fluent style must live in a dictionary merged **into each window**
  (`Themes/Controls.xaml`), not in `App.xaml`: the window loads after the Fluent theme, so `BasedOn` resolves to
  the Fluent style.
- Theme choice belongs in Settings (picked once), previewed live, reverted on Cancel; it should not need a restart.
- Lesson learned: a third-party Fluent library (WPF UI) was adopted and then reverted — almost the same look, an
  extra dependency, more startup work, and async message boxes everywhere.

## Own title bar with WindowChrome

```xml
<WindowChrome.WindowChrome>
  <WindowChrome CaptionHeight="32" ResizeBorderThickness="6"
                GlassFrameThickness="0,0,0,1" UseAeroCaptionButtons="False" />
</WindowChrome.WindowChrome>
```

- The 1 px bottom frame extension keeps the system shadow and rounded corners. **Do not use
  `GlassFrameThickness="-1"`**: Windows then draws its own caption buttons underneath yours (doubled buttons; neither
  `UseAeroCaptionButtons=False` nor `SetWindowThemeAttribute` hides them). The price: no Mica; paint the window with
  a solid themed background brush.
- A reusable `TitleBar` control: icon, title (dimmed while inactive), slots for content marked
  `WindowChrome.IsHitTestVisibleInChrome="True"`, and Minimize / Maximize-Restore / Close (46 × 32, Segoe Fluent
  Icons, Close red on hover). Dialogs show Close only.
- When maximized, add the resize border thickness to the content margin — a custom-frame window extends past the
  screen by that much.
- **Windows 11 snap layouts** appear only over a region that hit-tests as `HTMAXBUTTON`. Hook the window procedure:
  return `HTMAXBUTTON` from `WM_NCHITTEST` over your Maximize button; drive its hover/pressed look from
  `WM_NCHITTEST` / `WM_NCMOUSELEAVE` (`TrackMouseEvent` with `TME_LEAVE | TME_NONCLIENT`), click on `WM_NCLBUTTONUP`, and swallow
  `WM_NCLBUTTONDOWN` there so Windows draws no classic button. Keep the interop in the `Windows` project.
- The OS still handles dragging, double-click maximize, Aero snap, resize from edges and the system menu
  (Alt+Space, right-click).

## Own message box

`System.Windows.MessageBox` can't be themed, labels buttons Yes/No and defaults to Yes. Build a small
`MessageWindow` (same title bar and tokens): heading, **selectable** message text, an icon badge by kind
(question / risky / error), up to two buttons **named after the action** ("Delete", "Connect anyway").

`IDialogService`: `Confirm` (action default), `ShowError` (OK), and `ConfirmRisky(title, message, confirmText)`
where **Cancel is the default and carries the accent**: Enter and Esc cancel; the action needs a click or Tab.
Use it for delete, overwrite, take-over, production targets, closing with work in progress. The highlighted button
is always the one Enter picks. Keep calls synchronous (`ShowDialog`). Keep the system Open/Save file dialogs.

## Dialogs and layout

- Long settings/editor dialogs: a `TabControl` restyled as a **side navigation** (tabs on the left) rather than a
  custom ListBox — keeps Ctrl+Tab / Ctrl+PgDn / arrows and accessibility for free.
- Each tab page in its own `ScrollViewer` with a **~20 px right gutter**: the Fluent scroll bar overlays content and
  otherwise covers input fields.
- Size text boxes to their content (port, name) and pair related fields on one row instead of full-width boxes.
- Footer: error text on the left, Save/Cancel on the right, outside the tabs so an error is visible on any tab; a
  failed save switches to the tab with the problem and focuses the field.
- **Every radio-button group needs its own `GroupName`** — otherwise all radios in one panel form one group and a
  click in one section silently changes another.
- Read-only previews of generated values (e.g. a connection string) mask passwords as `********`.

## Keyboard

- Ctrl+S saves and Esc cancels in every dialog (`ApplicationCommands.Save`, `IsCancel`).
- Main window: search (Ctrl+F), a quick-open palette (Ctrl+K), F2 edit, Del delete (with confirmation), F5 refresh,
  Ctrl+, settings, Ctrl+N new, copy shortcuts. Show the shortcut on the button for short ones, in the tooltip for
  long ones.
- Put rarely used destructive actions in a "More" menu.

## View-source tests (Windows test project)

Parse the XAML (copied to the test output, see project-setup.md) with `XDocument` and assert:

- every `RadioButton` has a `GroupName`, with the expected groups and counts;
- tab names and order; every tab's `ScrollViewer` uses the gutter style;
- no fixed colours in views (no `#RRGGBB` or named colours in `Foreground`/`Background`/`Fill`/… attributes);
- both colour dictionaries define the same keys, and every `App.*` resource used by a view exists;
- every shortcut shown in the window is actually bound in `InputBindings`.

They are cheap, run in CI, and catch bugs that otherwise only show up when clicking through the app.
