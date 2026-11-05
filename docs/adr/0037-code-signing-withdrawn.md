# ADR-0037: Code signing withdrawn: releases stay unsigned for now

* Status: Accepted
* Date: 2026-11-05
* Requirements: NFR-06 (withdrawn)
* Supersedes: ADR-0036

## Context

ADR-0036 added signing through SignPath Foundation, gated on repository settings that would be filled in after the
project was approved. SignPath Foundation rejected the application, so the signing steps, the SignPath artifact
configuration and the public code signing policy page would only be dead configuration.

## Options considered

1. **Keep the gated steps** — no cost while unused, but documents a process that cannot run and a policy page for a
   signer the project does not have.
2. **Remove SignPath now** and revisit signing with another provider later (Azure Trusted Signing, an own OV
   certificate such as Certum Open Source).

## Decision

Remove SignPath (option 2): the release workflow publishes unsigned exes as before ADR-0036; the
`.signpath/` configuration and the code signing policy page are deleted; NFR-06 is withdrawn (the ID is not reused).
The installation guide explains SmartScreen and how to unblock the zip before unzipping.

## Consequences

* SmartScreen keeps warning on new versions; users unblock the zip once per download.
* Signing with another provider needs a new ADR and a new requirement ID.
