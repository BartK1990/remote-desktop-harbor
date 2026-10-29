# ADR-0008: Launch mstsc.exe, hand credentials over via Credential Manager

* Status: Accepted
* Date: 2026-10-29
* Requirements: FR-10, FR-11, FR-12, NFR-12

## Context

RDP sessions must open as normal Windows Remote Desktop windows (not embedded), already authenticated, without
admin rights and without exposing the password.

## Options considered

1. Embedded ActiveX RDP control (MsRdpClient) — explicitly not wanted; also ties session lifetime to our app.
2. `.rdp` file with `password 51:b:` (DPAPI blob) — works but writes a secret to disk.
3. `cmdkey /generic:TERMSRV/host /pass:...` then `mstsc` — password visible in the process list.
4. **`CredWrite` (TERMSRV/host, domain-password type) via P/Invoke, then `mstsc.exe <temp>.rdp`.**

## Decision

Option 4:

1. Build a temporary `.rdp` from the entry (`RdpFile.Build`): defaults, the entry's own settings, then app-managed
   keys (`full address`, `username`). Password keys are always stripped.
2. Write the password into the user's Credential Manager as `TERMSRV/<host>`, persistence **Session** (gone at
   logoff even if the app crashes) unless "keep credential" is enabled (then Local Machine persistence).
3. Start `%SystemRoot%\System32\mstsc.exe <file>`. Track the process for presence (ADR-0010).
4. After 30 s delete the temp file and (default) the credential; stale temp files are swept at startup.

## Consequences

* Environments that block saved credentials for RDP (Credential Guard / "Allow delegating saved credentials"
  policy) will still prompt; users can paste via "Copy password".
* Reconnects after the credential is removed may prompt — users who need this enable "keep credential".
* Process exit is used as "session ended"; closing the window (disconnect) counts as ended.
