# ADR-0038: Built-in file transfer that resumes after dropped connections

* Status: Accepted
* Date: 2026-11-05
* Requirements: FR-60, FR-61, NFR-12, NFR-30

## Context

Engineers often copy installers, logs and dumps to and from the servers they connect to. Over VPNs and slow links
copies of large files break; Explorer and `mstsc` drive redirection then start over from zero. The app opens
`mstsc.exe` as its own window (FR-10), so it cannot use the RDP virtual channel for files.

## Options considered

1. **RDP drive redirection** — needs no code, but lives inside the RDP session, cannot resume and is often disabled.
2. **robocopy /Z** — restartable mode exists, but it means a child process, credentials for the share on a command
   line or a separate `net use`, and progress parsed from text.
3. **Own chunked copy over SMB (UNC paths)** with a journal — chosen.

## Decision

* The transfer reaches the host over SMB: a UNC folder such as `\\host\C$\Users\Public\Downloads` (default, editable,
  remembered as `TransferRemoteFolder`). Before every attempt the share is connected with the entry's selected account
  through `WNetAddConnection2` (`Harbor.Windows/Transfer/NetworkShareConnector`); the password goes straight to Windows,
  never to a command line or disk (NFR-12). An existing connection with other credentials is used as it is.
* OS-neutral logic lives in `Harbor.Core/Transfer` and is tested on any OS (NFR-30):
  * `ResumableFileCopier` copies in 4 MiB chunks into `<name>.harbor-part`. After each chunk is written **and
    flushed** it records the byte count and the chunk's SHA-256. Before resuming it trims the partial file to the
    recorded length and checks the last recorded chunk against its hash (stepping back past chunks that did not
    really reach the disk). A source whose length or last write time changed starts over. At the end the partial file
    gets its final name; an existing file is only replaced when the user agreed.
  * `TransferJournal` keeps each job as `<id>.json` in `%LOCALAPPDATA%\RemoteDesktopHarbor\transfers`, replaced
    atomically after every chunk — "always knowing where the copy was left off", across drops, app restarts and
    crashes. It holds paths, sizes, hashes and the entry/account id, no secret.
  * `TransferQueue` runs one job at a time. Connection problems (`IOException`, `TimeoutException`) are retried
    automatically with growing waits (2 s → 30 s) from the last recorded chunk; after 10 minutes without a new chunk
    the job is paused and can be resumed later. Other errors (missing source, destination exists, access denied) fail
    the job. Jobs unfinished when the app closed come back paused.
* UI: **Transfer files…** (Ctrl+Shift+T) opens one non-modal File transfer window for the selected entry: Upload
  files…, Upload folder…, Download…, and a list with progress, Pause, Resume, Cancel (deletes the partial file),
  Resume all and Clear done. Closing the app with unfinished transfers asks first.

## Consequences

* Works wherever SMB to the host works (admin shares need an admin account); it does not tunnel through RDP.
* A resumed copy re-reads only one chunk of the destination, so resuming is cheap even over slow links; integrity of
  older chunks relies on them having been flushed before they were recorded.
* Future work: parallel transfers, bandwidth limits, a full read-back verification option.
