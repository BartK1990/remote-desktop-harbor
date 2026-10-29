# ADR-0011: CI and release on GitHub Actions, win-x64

* Status: Accepted
* Date: 2026-10-29
* Requirements: NFR-04, NFR-30

## Decision

* **CI** (`.github/workflows/ci.yml`, every push/PR): on `windows-latest` — restore, build (warnings as errors),
  run all tests, publish the win-x64 self-contained exe and upload it as a build artifact.
* **Release** (`.github/workflows/release.yml`, on tag `v*`): same build with the version from the tag, produces
  `RemoteDesktopHarbor-<ver>-win-x64.zip` (self-contained) and `RemoteDesktopHarbor-<ver>-win-x64-requires-dotnet10.zip`
  (needs .NET 10 Desktop Runtime), with SHA-256 checksums, attached to a GitHub Release.
* Central package versions (`Directory.Packages.props`) and a pinned SDK (`global.json`).

## Consequences

* Code signing and MSIX packaging are later steps (secrets needed).
* Core tests also run on Linux/macOS locally thanks to `EnableWindowsTargeting`.
