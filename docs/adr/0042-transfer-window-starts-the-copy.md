# ADR-0042: Harbor's transfer window starts the copy in the session

* Status: Accepted
* Date: 2026-11-08
* Requirements: FR-65, FR-63, FR-61, FR-10, FR-33, NFR-12
* Amends: ADR-0041 (adopts its option 3 for transfers only), ADR-0039 and ADR-0041 (chunk size)

## Context

With ADR-0041 a transfer over port 3389 only needs the user to press Win+R, Ctrl+V and Enter in the session. Users still
asked to skip that step. Remote Desktop has no way for the client to write a file on the server: drive redirection and
clipboard file copy only *offer* files, so something running in the session must pull them. A built-in Remote Desktop
client therefore does not remove the helper; it can only start it. Writing an own client (protocol stack, FreeRDP) is far
too large for that.

## Options considered

1. **Remote Desktop ActiveX control (mstscax.dll) in an app window, for transfers only**, which types Win+R, Ctrl+V and
   Enter into the session after sign-in. Ships with Windows; the app gets the sign-in event and controls the keyboard.
   Typing is best effort: it needs the window in front and the desktop ready.
2. **`alternate shell` / RemoteApp** to run the helper as the session's program. Ignored or blocked by many servers, and
   does not reach a session that is already open.
3. **Own client or virtual channel** with an agent in the session (ADR-0041 option 2): a native plug-in and agent to build
   and deploy.

## Decision

Option 1, on by default (`TransferAutoStart`), with FR-63's mstsc flow as the fallback.

* `RdpTransferSessionHost` (Harbor.Windows) creates the newest registered *NotSafeForScripting* client class
  (`MsRdpClient10`, then `9`) in a WinForms `AxHost` on its own STA thread. Settings are set late-bound: host, port, user,
  domain, `ClearTextPassword` (in memory only, NFR-12), authentication level, administrative session, CredSSP, auto
  reconnect, smart sizing, `KeyboardHookMode = 1` (Windows keys go to the session). Drives: the control only offers *all*
  drives (`RedirectDrives`); the per-drive collection is a non-scriptable interface not worth the risk, so the window
  shares every drive and is closed as soon as the copy is done or cancelled.
* Events come from `IMsTscAxEvents` (connecting, login complete, disconnected, fatal error). After login complete and 4 s
  for the desktop, the window puts `start.cmd /auto` on the clipboard and sends Win+R, then 1.5 s later Ctrl+V and Enter
  with `SendInput`, each step only while the window is in front (`GetForegroundWindow`), so nothing is typed into another
  program. Otherwise the user clicks **Start copy**; typing Win+R, Ctrl+V, Enter by hand still works.
* `start.cmd /auto` runs the helper with `-NoPause`. The helper holds `transfers\running\<id>.lock` on the server while it
  runs; a second start for the same transfer exits (code 4), so starting it again after a reconnect is safe. The window
  does not start a transfer that is done or cancelled.
* The launch goes through the same path as Connect (`ConnectEntryAsync` with a launch delegate): takeover and production
  questions and presence (FR-33) apply, and presence ends when the window closes.
* Entries behind a Remote Desktop Gateway connect with mstsc (FR-63): the window does not set up gateways.
* **Chunk size 4 MiB → 1 MiB** for both the network share copier and the helper: on slow links (210 ms, drive
  redirection) a dropped connection loses less, and progress moves more often. The size is stored in each journal or
  plan, so transfers started before keep theirs.

## Consequences

* Over port 3389 a transfer needs no typing in the session in the common case. When keys cannot be sent (window not in
  front, desktop slow to start, unusual keyboard layout for Ctrl+V), the user clicks Start copy or types the command.
* FR-10 (mstsc, not embedded) has an exception for transfers. The window is plain: no full screen, multi-monitor or the
  entry's other `.rdp` settings.
* While the window is connected every drive of the PC is visible to the server (stated in the UI and docs); it is closed
  when the copy ends.
* Connecting with the same account takes over an open session of that account, as with mstsc (FR-33 warns first).
* Harbor.Windows uses WinForms (for `AxHost`). The window and the key sending only run on Windows and are tested by hand;
  the settings, the start command and the helper's lock are covered by tests (NFR-30).
* More checkpoints: four times as many journal writes and hashes per file; negligible next to the copy.
