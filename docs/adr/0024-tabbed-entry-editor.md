# ADR-0024: Entry editor on tabs with compact, paired fields

* Status: Accepted
* Date: 2026-10-30
* Requirements: FR-09, FR-13, FR-46

## Context

With additional accounts (ADR-0019) and the RDP settings form (ADR-0022) the entry editor became one long scrolling
page of about 1400 px in a 760 px window: the basic fields, the accounts, three groups of RDP settings and the notes.
Users have to scroll past the RDP options to reach the notes, and the overlay scroll bar of the built-in Fluent theme
(ADR-0021) covers the right edge of the input fields — the same problems ADR-0023 solved for Settings.

## Options considered

1. **Keep one page, collapse the RDP groups into expanders** — shorter, but the accounts and the scroll bar problem
   stay, and the dialog looks different from Settings.
2. **Two tabs (Entry, RDP settings)** — fewer tabs, but the RDP tab would still need a long scroll.
3. **A `TabControl` with five tabs**, the same pattern as Settings (ADR-0023): own scroll viewer and right gutter per
   tab, related fields on one row.

## Decision

Option 3.

* Tabs: **General** (type, name + folder, host + port, domain + user name, password, notes), **Accounts** (additional
  accounts), **Display**, **Local resources**, **Connection** (incl. *Other .rdp settings (advanced)*).
* The three RDP tabs are collapsed for a credentials-only entry; the type choice is on General, so the selected tab
  never disappears under the user.
* Paired fields share a grid row: name + folder, host + narrow port, domain + user name. Each additional account is a
  single row (domain, user name, password, Remove) with *Change stored password* below the password. Colour depth and
  server authentication get fixed-width boxes.
* Each tab page has a 20 px right margin inside its `ScrollViewer`, so the overlay scroll bar never covers inputs.
* The error text and Import .rdp… / Save / Cancel stay below the tab control, so a validation error is visible
  whatever tab is open. The window opens at 600 × 640 instead of 580 × 760, enough for the General tab without scrolling.

## Consequences

* A validation error can refer to a field on another tab; the messages name the field (host, account, resolution).
* No view-model or storage change. New editor fields must go on the matching tab; `ViewSourceTests` checks the tab
  list, which tabs are RDP-only and the scroll gutter.
* The docs mockup (`website/mockups/app-mockups.html`) has one shot each for the General, Accounts and Display tabs.
