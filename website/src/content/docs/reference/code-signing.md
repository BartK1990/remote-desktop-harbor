---
title: Code signing policy
description: How Remote Desktop Harbor releases are signed, who can approve a signature, and what the app sends over the network.
---

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by
[SignPath Foundation](https://signpath.org/).

Release builds of `RemoteDesktopHarbor.exe` are signed with an Authenticode certificate issued to
**SignPath Foundation**. Only exes built from this repository's source by the
[release workflow](https://github.com/BartK1990/remote-desktop-harbor/blob/master/.github/workflows/release.yml) on
GitHub-hosted runners can be signed; every release signature is approved by hand. See
[CI and releases](../../development/ci-and-releases/) for the technical details.

:::note
Releases published before signing was enabled are not signed. Check a download with
`Get-AuthenticodeSignature .\RemoteDesktopHarbor.exe`: a signed exe shows the status **Valid** and SignPath
Foundation as the signer.
:::

## Team roles

| Role | Members |
|---|---|
| Committers and reviewers | [Bartosz Kuriata](https://github.com/BartK1990) |
| Approvers | [Bartosz Kuriata](https://github.com/BartK1990) |

Changes from other contributors are merged through reviewed pull requests.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user
or the person installing or operating it.

The app only connects to systems you configure:

- the **shared database** you set up in Settings (see [Shared database](../../team/shared-database/));
- **Microsoft Entra ID**, if you choose Entra ID sign-in (see [Entra ID](../../team/entra-id/));
- the **remote computers** you connect to, through Windows Remote Desktop (`mstsc.exe`).

There is no telemetry, no update check and no other network traffic.
