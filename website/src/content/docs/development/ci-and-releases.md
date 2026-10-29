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
4. Zips both, writes `SHA256SUMS.txt`, and creates a GitHub Release with generated notes. Versions with a `-` suffix
   are marked as pre-releases.

To release:

```bash
git tag v0.2.0
```

```bash
git push origin v0.2.0
```

## Documentation

`.github/workflows/docs.yml` builds this site on pull requests that touch `website/`, and deploys it to GitHub Pages on
pushes to `master`.

:::note[One-time setup]
In the repository's **Settings → Pages**, set **Source** to **GitHub Actions**.
:::

## Future work

- Code signing of the exe (SmartScreen reputation).
- MSIX packaging for the Microsoft Store.
- A containerized SQL Server job to test the `SqlServer` provider in CI.
