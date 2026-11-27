# ADR-0055: The framework-dependent build is published without ReadyToRun

* Status: Accepted
* Date: 2026-11-27
* Requirements: NFR-01, NFR-02

## Context

The release publishes two builds (ADR-0003): the self-contained exe and a small framework-dependent one
(`-requires-dotnet10`) that runs on the .NET 10 Desktop Runtime installed on the PC. Both were published with
ReadyToRun. ReadyToRun code is compiled against the runtime on the build machine; the framework-dependent exe then runs
on whatever 10.0.x patch the user has. 0.18.2 crashed at start on a PC with .NET 10.0.15 (`coreclr.dll`, exception
`0xc0000602`, no managed stack trace), and started normally with `DOTNET_ReadyToRun=0`. The self-contained exe, which
carries the runtime it was compiled against, ran fine on the same PC.

## Options considered

1. Keep ReadyToRun and pin the build machine's runtime to the newest patch — the user's runtime can still be older or
   newer than that; the crash comes back with the next patch.
2. Publish the framework-dependent build without ReadyToRun — the app's own code is compiled at start by the JIT
   (tiered compilation, TieredPGO); the framework itself is still precompiled in the installed runtime.
3. Stop publishing the framework-dependent build — users who want the small download lose it.

## Decision

Option 2. `release.yml` publishes the framework-dependent build with `PublishReadyToRun=false`. The self-contained build
keeps ReadyToRun: its precompiled code always runs on the runtime it was made for.

## Consequences

* The framework-dependent exe starts a little slower (the app's own code is JIT-compiled); the self-contained build,
  the default, is unchanged.
* The framework-dependent build no longer depends on which 10.0.x patch the build machine and the user's PC have.
