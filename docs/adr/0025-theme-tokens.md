# ADR-0025: Harbor colour and type tokens on top of the built-in Fluent theme

* Status: Accepted
* Date: 2026-10-31
* Requirements: FR-02, FR-08, FR-33
* Builds on: ADR-0002, ADR-0021

## Context

The views hard-coded the colours the Fluent theme does not provide: `OrangeRed` for errors and the offline status,
`#33FF8C00` / `#22FF8C00` for the "in use" badge and the active-sessions box. These are fixed values, so they look
the same in light and dark mode and some fail contrast in one of them. Users are engineers who scan many hosts:
the tree showed only names, so hosts (and non-default ports) were hidden behind a tooltip, and the long "In use
by …" badge crowded out the name. A UI redesign is planned in steps; every step needs one shared vocabulary of
colours, fonts and sizes.

## Options considered

1. **Keep fixed colours per view** — no work; but no dark variants, duplicated values, no place to change them.
2. **Our own full palette (neutrals too)** — complete control; but it fights the Fluent theme (window background,
   Mica, accent colour from Windows) and doubles the work for every control.
3. **Tokens only where Fluent has nothing** — neutrals, accent and control colours keep coming from the Fluent
   theme keys (`TextFillColor*`, `CardBackgroundFillColor*`, `AccentFillColor*`, …); Harbor adds a small set of
   semantic colours (in use, online, offline, error) with a light and a dark value, plus type and size tokens.
4. **A third-party theme library** — rejected again for the reasons in ADR-0021.

## Decision

Option 3.

* `Themes/Tokens.xaml` holds what does not depend on light/dark: the monospace font (`Cascadia Mono`, then
  `Consolas`), the icon font (Segoe Fluent Icons / MDL2 Assets), font sizes, the tree row height (24 px) and shared
  styles (`Harbor.Text.Mono`, `Harbor.Text.Icon`, `Harbor.Text.Error`, `Harbor.StatusDot`, …).
* `Themes/Colors.Light.xaml` and `Themes/Colors.Dark.xaml` define the same `Harbor.*Brush` keys. Text colours reach
  4.5:1 and marks 3:1 on the Fluent backgrounds of their theme.
* `App.xaml` merges `Tokens.xaml` and one colour set. `ThemeService` sets `ThemeMode` as before and swaps the colour
  set; for *System* it reads the Windows app mode (`AppsUseLightTheme`, `Harbor.Windows.Theming.WindowsAppMode`) and
  swaps again on a Windows light/dark change (`SystemEvents.UserPreferenceChanged`). Views use the colour tokens
  through `DynamicResource` so a swap applies live.
* Views never contain fixed colour values; a view-source test enforces it and checks that both colour sets have the
  same keys and that every `Harbor.*` resource a view uses exists.
* Anything an engineer would type in a terminal (host, port, user name) is shown in the monospace font.
* Tree rows (FR-02) show an icon (folder, RDP connection, credentials, account), the name and, on the right, the
  host with its port when it is not 3389. An entry or account in use shows a dot and the user's name instead of the
  host (FR-33); the full "In use by …" text is the accessible name and the details are in the tooltip.

## Consequences

* Dark mode no longer shows light-tuned orange; error and offline texts are readable in both themes.
* The following redesign steps (toolbar, details pane, dialogs) reuse the same tokens.
* The tree row is 2 px taller (24 instead of 22 px) to fit the icons; still one low row per item (ADR-0017).
* While an entry is in use its host is not shown in the row; it stays in the tooltip and the details pane.
