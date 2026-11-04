# ADR-0036: Release exes code-signed through SignPath Foundation

* Status: Accepted
* Date: 2026-11-04
* Requirements: NFR-06, NFR-02, NFR-04

## Context

Releases ship an unsigned `RemoteDesktopHarbor.exe` (ADR-0003, ADR-0011). Windows marks downloaded files with the
Mark of the Web, so SmartScreen warns on every new version, users have to unblock the exe after each update, and
Windows 11 Smart App Control can block an unsigned exe outright. ADR-0011 left code signing as a later step.

## Options considered

1. **SignPath Foundation** — free for OSI-licensed open-source projects; the certificate is issued to SignPath
   Foundation and the key stays in their HSM; signing is a GitHub Action that only signs artifacts built by this
   repository's workflow on GitHub-hosted runners (origin verification); release signing needs a manual approval.
   Requires a public code signing policy and approval of the project.
2. **Azure Trusted Signing** — cheap and easy in CI, but paid monthly and identity validation is limited by country.
3. **Own OV/EV certificate** (e.g. Certum Open Source) — certificate in the author's name, but the key must live on a
   hardware token or a cloud HSM, which makes CI signing harder; yearly cost.
4. **Stay unsigned** — no cost, but the warnings above stay.

## Decision

Sign release exes through SignPath Foundation (option 1):

* `release.yml` uploads both builds' `RemoteDesktopHarbor.exe` (`sc/`, `fdd/`) as one artifact, submits it with
  `signpath/github-action-submit-signing-request`, waits for the signed artifact, checks each exe with
  `Get-AuthenticodeSignature` (the job fails unless the status is `Valid`) and only then zips and checksums.
* Signing runs only when the repository variable `SIGNPATH_ORGANIZATION_ID` is set, so releases keep working
  (unsigned) until the project is approved. The API token is the secret `SIGNPATH_API_TOKEN`; project, signing
  policy and artifact configuration slugs are variables with defaults.
* The artifact configuration is kept in `.signpath/artifact-configuration.xml` and mirrored into SignPath.
* The code signing policy (team roles, privacy statement) is published on the documentation site and linked from
  the README, as SignPath Foundation requires.

## Consequences

* Signed releases no longer trip "unknown publisher"; SmartScreen reputation still builds up per certificate, so the
  first downloads may see a warning for a while.
* The signer shown by Windows is SignPath Foundation, not the author.
* A release waits for an approver to approve the signing request in SignPath (up to the 60-minute timeout).
* Only exes built by the release workflow can be signed; locally built exes stay unsigned.
* MSIX packaging (ADR-0012) can reuse the same signing project later.
