# ADR-0043: The version lives in `Directory.Build.props`; the release tag must match it

* Status: Accepted
* Date: 2026-11-09
* Requirements: FR-07, NFR-04

## Context

`Directory.Build.props` set `<Version>0.1.0</Version>`, but the release workflow (ADR-0011) overrode it with
`-p:Version` taken from the pushed tag or a manual input. Nothing ever bumped the value in the code, so every build that
was not a release — local, CI, a build from `master` after 0.3.0 shipped — still called itself 0.1.0, and the version in
the repository could silently contradict the released one.

## Options considered

1. Tag is the source of truth (status quo) — no manual bump, but the version in the code goes stale and misleads.
2. Tag is the source of truth, dev builds get a placeholder (`0.0.0-dev` or `git describe`) — honest, but local builds
   never carry a real version and `git describe` needs full history.
3. Code is the source of truth, the tag is checked against it — one manual bump per release, visible and reviewed in the
   diff; every build of a commit reports the same version.

## Decision

Option 3. `<Version>` in `Directory.Build.props` is the only place the version is written. The release workflow reads it
with `dotnet msbuild -getProperty:Version`, no longer passes `-p:Version`, and fails when a pushed tag is not exactly
`v<Version>`. A manual run takes no input: it releases the version in the code and creates the tag, failing if the tag
already exists.

## Consequences

* Releasing is: bump `<Version>`, merge, tag (or run the workflow by hand).
* Forgetting the bump makes the release fail early instead of shipping a mismatched version.
* Builds between releases report the last bumped version; bump right after a release (e.g. to `0.3.0-dev`) if dev builds
  should be told apart.
