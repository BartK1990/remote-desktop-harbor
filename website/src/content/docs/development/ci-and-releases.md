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

The version lives in the code: `<Version>` in `Directory.Build.props`. Local, CI and release builds of the same commit
all report it, and the app shows it in the title bar and Settings → About.

`.github/workflows/release.yml` runs when a tag `v*` is pushed, or manually (**Run workflow**, which creates the tag
`v<version>` from the chosen branch):

1. Reads the version from the project and validates it (`1.2.3` or `1.2.3-beta.1`). A pushed tag must be exactly
   `v<version>`; a manual run fails if that tag already exists.
2. Runs the tests.
3. Publishes the self-contained build and the framework-dependent (`-requires-dotnet10`) build.
4. Zips both, writes `SHA256SUMS.txt`, and creates a GitHub Release with generated notes. Versions with a `-` suffix
   are marked as pre-releases.

To release, bump `<Version>` in `Directory.Build.props` (for example to `0.2.0`), merge that change, then tag the
commit that contains it:

```bash
git tag v0.2.0
```

```bash
git push origin v0.2.0
```

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

- Code signing of the exe (SmartScreen reputation); see ADR-0037.
- MSIX packaging for the Microsoft Store.
- A containerized SQL Server job to test the `SqlServer` provider in CI.
