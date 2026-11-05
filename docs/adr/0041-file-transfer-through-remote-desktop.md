# ADR-0041: File transfer through the Remote Desktop connection

* Status: Accepted
* Date: 2026-11-05
* Requirements: FR-63, FR-60, FR-61, FR-10, NFR-02, NFR-12, NFR-30
* Amends: ADR-0039 (adds a second way to copy; the network share stays)

## Context

ADR-0039 copies over SMB (port 445). Many networks only open the Remote Desktop port (3389) to servers — a user's
`Test-NetConnection <server> -Port 445` failed while Remote Desktop worked — so File transfer did not work at all there.
The app launches `mstsc.exe` as its own window (FR-10) and cannot use its virtual channels directly.

## Options considered

1. **Drive redirection plus a helper inside the session** — Remote Desktop already carries the PC's drives to the
   server as `\\tsclient\<drive>`; a small helper running in the session copies between them and the server's disk.
   Only port 3389, no admin rights, nothing installed on either side. The user starts the helper in the session once.
2. **Own dynamic virtual channel**: an mstsc client plug-in (COM DLL registered per user) plus an agent in the session.
   Fully driven from the app, but a native plug-in and an agent to build and deploy.
3. **Embed the Remote Desktop ActiveX control** — full control of the channel, but reverses FR-10.

## Decision

Option 1.

* The app writes into `rdp-transfers` in its data folder: the helper `harbor-copy.ps1` (embedded in `Harbor.Core`), a
  `plan.json` per transfer (sources as `\\tsclient` paths or, for a download, the PC folder; the server folder; overwrite
  flag; chunk size) and a `start.cmd`. It then connects the entry with a launch-only `.rdp` override that shares the
  drives the plan needs plus the data folder's drive (`drivestoredirect`, merged with the entry's own), and puts the
  start command (`\\tsclient\C\…\start.cmd`, short enough for Win+R) on the clipboard. A session already open with the
  same account is reconnected with the drives.
* The user presses Win+R, Ctrl+V, Enter in the session. `start.cmd` runs Windows PowerShell with the helper loaded as a
  script block, so an execution policy that blocks unsigned scripts from network paths does not stop it. PowerShell is
  on every supported Windows Server, so nothing is installed; a downloaded file picker runs on the server for downloads.
* The helper mirrors `ResumableFileCopier` (ADR-0039): 4 MiB chunks into `<name>.harbor-part`, each flushed before it is
  recorded with its SHA-256; before resuming it trims the partial file and checks the last chunk; a changed source starts
  over; the partial file is renamed at the end. **Its journal lives on the server**
  (`%LOCALAPPDATA%\RemoteDesktopHarbor\transfers\<id>.json`), the side that keeps running: when Remote Desktop drops,
  the session and the helper survive, `\\tsclient` disappears, and the helper waits (however long) for it to come back,
  then continues. Running the start command again also resumes. Errors while the PC is reachable are retried for 30
  minutes; a missing source, an existing destination or access denied fail the file.
* The helper writes `status.json` next to the plan after every chunk (best effort); the app shows it in the File transfer
  window every 2 s. A `cancel` file asks the helper to stop and delete its partial files.
* Plans hold paths and sizes only; no password goes into any file or command line (NFR-12). The connection uses the
  entry's credentials as any launch does (Credential Manager, FR-11).
* Remote Desktop is the default way to copy (`TransferMethod`); the network share remains for networks that allow it.

## Consequences

* Works wherever Remote Desktop works, as long as the server allows drive redirection (a policy can disable it).
* While the transfer's session is connected, the shared drives — at least the data folder's drive — are visible to the
  server. This is stated in the UI and the docs.
* The user starts each transfer in the session; the app cannot pause it (closing the helper's window pauses, the start
  command resumes).
* Throughput is limited by Remote Desktop's drive redirection; a 210 ms link is slow but steady.
* The helper is PowerShell 5.1 compatible and is tested from `Harbor.Core.Tests` with Windows PowerShell in CI and
  PowerShell 7 elsewhere (NFR-30).
