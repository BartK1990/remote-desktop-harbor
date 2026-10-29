# ADR-0003: Distribution — portable self-contained win-x64 exe, no install

* Status: Accepted
* Date: 2026-10-29
* Requirements: NFR-01, NFR-02, NFR-04

## Context

Users must run the app from a standard (non-admin) account without installing anything — including the .NET runtime,
whose installer normally needs admin rights.

## Decision

* Default artifact: `RemoteDesktopHarbor.exe`, **self-contained single file, win-x64, ReadyToRun**, published with
  `src/Harbor.App/Properties/PublishProfiles/win-x64.pubxml`. Native libraries (SQLite, MSAL runtime, WPF) are
  bundled and self-extracted to the per-user temp folder on first run (no admin).
* Single-file **compression is off**: it would cut size ~50% but decompresses on every start (startup is the priority).
  Release zips are compressed anyway.
* A second, small **framework-dependent** zip (`-requires-dotnet10`) is published for machines that already have the .NET 10 Desktop Runtime.
* Manifest `requestedExecutionLevel=asInvoker`; nothing is written outside the user's profile.
* Data location: `%LOCALAPPDATA%\RemoteDesktopHarbor`. **Portable mode**: a `portable.txt` next to the exe moves data
  to `.\data`. Encryption still binds data to the Windows user and machine (ADR-0005), so a portable folder copied to
  another PC cannot decrypt passwords — by design.

## Consequences

* Self-contained exe is ~170 MB (WPF + .NET runtime + SqlClient + MSAL broker); zipped ~70 MB. Trimming is not
  supported for WPF, so this is close to the floor for a no-install WPF app.
* Updates are "replace the exe". An auto-update mechanism or the Store (ADR-0012) can come later.
* Code signing of the exe is recommended for SmartScreen reputation (future CI secret).
