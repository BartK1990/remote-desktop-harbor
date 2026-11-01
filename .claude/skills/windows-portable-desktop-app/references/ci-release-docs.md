# CI, release, Store readiness, docs

## CI — `.github/workflows/ci.yml`

```yaml
name: CI
on:
  push: { branches: [main] }
  pull_request:
  workflow_dispatch:
permissions: { contents: read }
concurrency: { group: ci-${{ github.ref }}, cancel-in-progress: true }
env: { DOTNET_NOLOGO: true, DOTNET_CLI_TELEMETRY_OPTOUT: true }
jobs:
  build-test-publish:
    runs-on: windows-latest            # WPF/DPAPI tests only run here
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { global-json-file: global.json }
      - run: dotnet restore MyApp.slnx
      - run: dotnet build MyApp.slnx -c Release --no-restore
      - run: dotnet test MyApp.slnx -c Release --no-build --logger trx --results-directory TestResults
      - if: always()
        uses: actions/upload-artifact@v4
        with: { name: test-results, path: TestResults }
      - run: dotnet publish src/MyApp.App/MyApp.App.csproj -p:PublishProfile=win-x64 -o artifacts/win-x64
      - uses: actions/upload-artifact@v4
        with: { name: MyApp-win-x64, path: artifacts/win-x64/MyApp.exe, if-no-files-found: error }
```

Publishing in every CI run keeps the single-file/ReadyToRun profile working and gives testers a ready exe.

## Release — `.github/workflows/release.yml` (on tag `v*` or manual with a version)

Steps on `windows-latest`, `permissions: contents: write`:

1. Determine the version from the tag (strip `v`) or the dispatch input; validate
   `^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$`.
2. `dotnet test -c Release`.
3. Publish self-contained with `-p:PublishProfile=win-x64 -p:Version=<v>`; optionally framework-dependent too.
4. Remove `*.pdb`, add README, `Compress-Archive` to `MyApp-<v>-win-x64.zip` (and
   `MyApp-<v>-win-x64-requires-dotnet10.zip`); write `SHA256SUMS.txt` from `Get-FileHash`.
5. `gh release create v<v> --generate-notes --target <sha> <files>`; add `--prerelease` when the version contains
   `-`. Build the PowerShell argument list explicitly — an empty `""` argument is taken by `gh` as an asset path.

The version passed to `-p:Version` becomes the app's informational version, so the About box matches the release.

Later steps: **code signing** (Authenticode, via a CI secret) for SmartScreen reputation; auto-update or Store.

## Microsoft Store readiness (keep it possible, package later)

Holds automatically if the rules are followed: per-user locations only (MSIX virtualizes them), no registry
writes, no services, no elevation, no machine-wide installs; normal process starts, DPAPI and Credential Manager
work in full-trust packaged apps. When needed: a packaging project or `dotnet publish` + `MakeAppx` producing an
MSIX with `runFullTrust`, signed; or submit the unpackaged Win32 app via an installer URL. Portable mode is off
inside MSIX. DPAPI-bound data survives Store updates.

## Spec and ADRs

- `docs/spec/SPEC.md`: functional (`FR-xx`) and non-functional (`NFR-xx`) requirements with **stable IDs** (never
  renumber or reuse), plus a traceability table (requirement → code → tests).
- `docs/adr/NNNN-title.md` in Nygard format (Context, Options considered, Decision, Consequences), indexed in
  `docs/adr/README.md`. Accepted ADRs are not rewritten; a new ADR supersedes or amends (and the old one's status
  says so). Reverting a decision is itself an ADR.
- Code and tests cite IDs (`// SPEC FR-12`, `See ADR-0005`). One PR = spec change + ADR + code + tests + docs page.
- Typical first NFRs for this kind of app: window < 1 s; no admin, no install, one exe; smooth with 10k items;
  CI/release win-x64; Store-publishable; secrets AES-256-GCM bound to user+machine; no plaintext secret on disk,
  command line or logs; logic tested on any OS.

## Docs site

Astro Starlight in `website/` (separate from the .NET build): Markdown pages, explicit sidebar in
`astro.config.mjs`, built-in search and light/dark. Use the regular doc layout for every page (no splash) so the
sidebar is always visible. A workflow builds it on PRs touching `website/` and deploys to GitHub Pages from the
default branch (Settings → Pages → Source = GitHub Actions). The site links to the spec/ADRs instead of copying them.

## Screenshots from the real app in CI

Hand-made mockups drift. Instead, a `tools/MyApp.Screenshots` console project (not shipped):

- runs next to the published exe in **portable mode** so it never touches real user data;
- seeds fictional demo data through the app's own services (and, if needed, a throwaway database such as SQL Server
  LocalDB on the runner);
- starts the app, sizes the window to the runner's screen, drives it with `SendInput` keyboard shortcuts, and
  copies each window's frame to a PNG;
- runs in a workflow on PRs touching the app (artifact) and, when dispatched with a "commit" option, commits the
  images. A changed shortcut must be changed in the tool too — another reason to keep shortcuts stable.

No demo-mode code paths in the shipped app.
