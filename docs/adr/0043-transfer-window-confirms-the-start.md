# ADR-0043: The transfer window confirms that the copy started in its own session

* Status: Accepted
* Date: 2026-11-09
* Requirements: FR-66, FR-65
* Amends: ADR-0042

## Context

Two problems with Harbor's transfer window (ADR-0042) in use:

1. **The app froze while the window was open.** `RdpTransferSessionHost.Open` runs on the app's UI thread and waited for
   the form's `HandleCreated` event. The ActiveX host's `EndInit` already creates the form's handle in the constructor,
   before the handler was attached, so the event never reached it and the UI thread waited until the window closed.
2. **The keys went to the wrong computer.** The window types Win+R, Ctrl+V and Enter into the session. When a Remote
   Desktop window is open full screen *inside* that session, it captures Windows key combinations and sends them on to
   the computer it is connected to; the clipboard is passed on too. The Run box opened there and failed (that computer
   has no such `\\tsclient` path), while Harbor said the copy had started.

The client cannot see which window is in front inside the session, so the second problem cannot be prevented reliably
by more keystrokes (Win+D, Alt+Tab and the like are captured the same way; Ctrl+Alt+Home may be taken by Harbor's own
control and send the next keys to this PC).

## Decision

* `Open` hands the window over as soon as it is constructed (its handle forced to exist), and waits at most 15 s.
* `start.cmd` first writes a `started` marker (the session's computer name) next to the plan, i.e. on this PC through
  the shared drive. Only the session Harbor is connected to can write it: on a computer one hop further, `\\tsclient`
  is the session's server, where the plan does not exist.
* The window deletes the marker before typing and, after Enter, polls for it for up to 15 s (new state `Checking`).
  Found: *Copy started*. Not found: `NeedsClick`, with a message that a full-screen Remote Desktop window in the session
  probably took the keys and should be minimized before clicking **Start copy**. It never types again by itself.

## Consequences

* The app's other windows stay usable while a transfer window is open.
* A start that went to the wrong place is reported instead of hidden; the user fixes it with one click after minimizing
  the inner window. The wrong computer still shows a "cannot find" error from its Run box, which is harmless.
* `start.cmd` writes one small extra file into the plan folder; it is deleted with the plan.
