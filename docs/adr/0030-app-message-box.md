# ADR-0030: The app's own message box; risky confirmations default to Cancel

* Status: Accepted
* Date: 2026-10-31
* Requirements: FR-33, FR-34, FR-51, FR-53
* Builds on: ADR-0021 (synchronous dialogs), ADR-0029 (title bar)

## Context

Confirmations and errors used `System.Windows.MessageBox`: system title bar and colours, "Yes/No" labels, and Yes
as the default button. For "take over Jane's session" or "connect to production" that default is the wrong way round:
a second press of Enter — the key that just started the connection — confirms it.

## Options considered

1. **Keep `MessageBox`, change the wording** — cannot change the default button or the look.
2. **Task dialog (`TaskDialogIndirect`)** — native, supports a default button; still system-styled and needs interop.
3. **An own `MessageWindow`** — a small WPF window with the app's title bar and tokens.

## Decision

Option 3. `Views/MessageWindow` shows a heading, a selectable message, an icon badge by kind (question, risky,
error; colours from the tokens) and up to two buttons named after the action. `IDialogService` keeps
`Confirm` (Yes default) and `ShowError` (OK), and adds `ConfirmRisky(title, message, confirmText)`, where **Cancel is
the default** and carries the accent: Enter and Esc cancel; the action takes a click or Tab to it. Delete, session
takeover, production and "close with sessions open" use it. The unhandled-exception handler uses the window too and
falls back to the system box if it cannot be shown. Calls stay synchronous (`ShowDialog`), as in ADR-0021.

## Consequences

* All app dialogs look alike in both themes.
* Confirming a risky action takes one key more (Tab) — intended.
* The Open-file dialog stays the system one.
