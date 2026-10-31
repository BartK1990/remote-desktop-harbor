# ADR-0027: Side navigation in the editor and Settings; theme chosen in Settings

* Status: Accepted
* Date: 2026-10-31
* Requirements: FR-08, FR-45, FR-46, NFR-12
* Amends: ADR-0023, ADR-0024 (layout only), ADR-0026 (toolbar)

## Context

ADR-0023 and ADR-0024 split Settings and the entry editor into top tabs. With six Settings sections and five editor
sections the tab strip gets crowded and the labels read as a sentence rather than a list. Some details still slowed
engineers down: the entry type was a pair of radio buttons, a stored password needed a "Change stored password"
checkbox before anything could be typed, `host:port` or `DOMAIN\user` pasted into one box was saved as typed, a
failed save only showed a message at the bottom, there was no Ctrl+S, and nothing showed what connection string the
database fields produce. The theme had three toggles in the main toolbar for a choice made once.

## Options considered

1. **Keep top tabs, add the fixes** — smallest change; the crowded strip stays.
2. **Replace the TabControl with a custom ListBox + content switcher** — full control; loses the TabControl's
   keyboard behaviour (Ctrl+Tab, Ctrl+PgDn, arrows) and its accessibility pattern, and every tab test.
3. **Keep the TabControl and draw its tabs as a side navigation** — a TabControl style (`Harbor.NavTabControl`, tabs
   on the left, `Harbor.NavTab` rows with the accent bar of the tree) changes only the look.

## Decision

Option 3, plus:

* **Settings:** sections Storage, Shared vault, Sign-in, Remote Desktop, Appearance and About (General is split).
  The storage mode is chosen with two cards (`Harbor.CardRadio`). Below the connection fields a read-only preview
  shows the connection string the fields produce, with every password replaced by `********`
  (`ConnectionStringMask`, NFR-12). The Test connection result is coloured with the online/error tokens. About has an
  Open button for the data folder.
* **Theme (FR-08):** moved from the toolbar to Settings → Appearance as a segmented control. Picking a theme applies
  it at once as a preview; Save keeps it, Cancel (or closing the dialog) restores the saved one. Save asks to restart
  only when something besides the theme changed: `SettingsViewModel.Apply` compares the settings before and after,
  ignoring the theme, and keeps the stored (DPAPI-protected) connection string while the fields still produce the
  string they showed on opening, because re-protecting gives new ciphertext.
* **Entry editor:** the type is a segmented control; host, port, domain, user name and folder use the monospace
  font. A stored password shows "Stored, encrypted" with Replace… and Remove (Keep stored password undoes either);
  accounts get Replace… and Keep. On save, `host:port` in Host becomes Host + Port (when Port is still 3389 or the
  same) and `DOMAIN\user` in User name becomes Domain + User name (when Domain is empty), also for accounts
  (`EntryInput`). A failed save opens the tab with the problem and focuses the field
  (`EntryEditorViewModel.ErrorLocation`).
* **Both dialogs:** Ctrl+S saves (`ApplicationCommands.Save`), Esc cancels; the error is shown on the left of the
  footer, the buttons on the right.
* Styles built on Fluent control styles (`BasedOn` the implicit style) move to `Themes/Controls.xaml`, merged into
  each window: a window loads after the Fluent theme is in place, so `BasedOn` resolves to the Fluent style.

## Consequences

* Tab names, order and the RDP-only tabs of the editor are unchanged; keyboard navigation works as before.
* The dialogs are wider (Settings 860 px, editor 820 px) to fit the navigation column.
* The toolbar is shorter; switching the theme takes two clicks more, which is fine for a one-time choice.
* Splitting on save means what is stored can differ from what was typed; the editor says so next to the fields.
