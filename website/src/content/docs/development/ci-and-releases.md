---
title: CI and releases
description: GitHub Actions workflows for building, testing, releasing and publishing the docs.
---

## CI

`.github/workflows/ci.yml` runs on every push and pull request, on `windows-latest`:

1. Restore, build (warnings as errors) and run all tests.
2. Upload the test results.
3. Publish the self-contained win-x64 exe and upload it as a build artifact.

## Releases

`.github/workflows/release.yml` runs when a tag `v*` is pushed, or manually with a version number (which creates the
tag `v<version>`):

1. Validates the version (`1.2.3` or `1.2.3-beta.1`).
2. Runs the tests.
3. Publishes the self-contained build and the framework-dependent (`-requires-dotnet10`) build with that version.
4. Signs both exes through SignPath Foundation (when configured, see below) and fails unless each signature is
   **Valid**.
5. Zips both, writes `SHA256SUMS.txt`, and creates a GitHub Release with generated notes. Versions with a `-` suffix
   are marked as pre-releases.

To release:

```bash
git tag v0.2.0
```

```bash
git push origin v0.2.0
```

### Code signing

Signing uses [SignPath Foundation](https://signpath.org/) (ADR-0036; policy: [Code signing policy](../../reference/code-signing/)).
The release job uploads `sc/RemoteDesktopHarbor.exe` and `fdd/RemoteDesktopHarbor.exe` as the *unsigned* artifact,
submits it to SignPath, waits (up to 60 minutes) for the approved signing request, verifies the signatures and
packages the signed exes. If the variable `SIGNPATH_ORGANIZATION_ID` is not set, these steps are skipped and the
release is unsigned.

One-time setup:

1. Apply for the project at [signpath.org](https://signpath.org/apply) and wait for approval.
2. In SignPath, install the GitHub trusted build system for the repository, create the project (slug
   `remote-desktop-harbor`), and set its artifact configuration to the content of
   [`.signpath/artifact-configuration.xml`](https://github.com/BartK1990/remote-desktop-harbor/blob/master/.signpath/artifact-configuration.xml).
3. Create a CI user in SignPath with submitter rights on the signing policy and generate its API token.
4. In the repository's **Settings → Secrets and variables → Actions**:

   | Kind | Name | Value |
   |---|---|---|
   | Secret | `SIGNPATH_API_TOKEN` | API token of the CI user |
   | Variable | `SIGNPATH_ORGANIZATION_ID` | SignPath organization ID — setting it turns signing on |
   | Variable (optional) | `SIGNPATH_PROJECT_SLUG` | default `remote-desktop-harbor` |
   | Variable (optional) | `SIGNPATH_SIGNING_POLICY_SLUG` | default `release-signing` (`test-signing` to try it out) |
   | Variable (optional) | `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG` | default: the project's default configuration |

When a release runs, approve the signing request in SignPath; the job then continues.

## Screenshots

`.github/workflows/screenshots.yml` captures the screenshots in this documentation from the **real app** on
`windows-latest`: [`tools/Harbor.Screenshots`](https://github.com/BartK1990/remote-desktop-harbor/tree/master/tools/Harbor.Screenshots)
creates a demo shared database on SQL Server LocalDB, seeds a fictional catalog and two colleagues' sessions, starts
the app in portable mode and drives it with the keyboard. Pull requests that touch the app get the images as the
*screenshots* artifact; run the workflow by hand with **commit** to update `website/src/assets/screenshots/`.

## Documentation

`.github/workflows/docs.yml` builds this site on pull requests that touch `website/`, and deploys it to GitHub Pages on
pushes to `master`.

:::note[One-time setup]
In the repository's **Settings → Pages**, set **Source** to **GitHub Actions**.
:::

## Future work

- MSIX packaging for the Microsoft Store.
- A containerized SQL Server job to test the `SqlServer` provider in CI.
