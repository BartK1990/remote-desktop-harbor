# ADR-0012: Microsoft Store readiness via MSIX packaging later

* Status: Accepted
* Date: 2026-10-29
* Requirements: NFR-05

## Context

The app may be published to the Microsoft Store later; we want to avoid choices that would block that.

## Decision

Keep the app Store-compatible now, package later:

* Only per-user locations (`%LOCALAPPDATA%`, temp) — MSIX virtualizes them transparently. No registry writes,
  no services, no elevation, no machine-wide installs.
* `mstsc.exe` is started as a normal process (allowed for full-trust desktop apps); Credential Manager and DPAPI work
  in packaged full-trust apps.
* When needed: add a Windows Application Packaging project (or `dotnet publish` + `MakeAppx`) producing an MSIX with
  `runFullTrust`, signed for Store submission. Alternatively the Store accepts unpackaged Win32 apps via an installer URL.

## Consequences

* Portable mode (`portable.txt`) is not applicable inside MSIX (install dir is read-only) — the Store build uses
  the default data location.
* DPAPI-bound data survives Store updates (same user/machine).
