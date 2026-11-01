---
name: windows-portable-desktop-app
description: Decisions, templates and pitfalls for building a Windows desktop app in .NET (WPF) that ships as one portable, self-contained exe, runs without installation or admin rights, starts fast, keeps secrets safe and stays Microsoft Store-ready. Use when starting, structuring or reviewing such an app — choosing the UI framework, setting up the solution and publish profile, the app manifest, per-user/portable data paths, settings and logging, DPAPI secret storage, theming (light/dark), custom title bar or dialogs, CI/release on GitHub Actions, or docs and screenshots — or when the user mentions "portable exe", "no install", "no admin", "single-file WPF", "self-contained Windows app".
---

# Windows portable desktop app

A field-tested set of decisions for a **Windows-only desktop app that users run by double-clicking one exe** —
no installer, no admin rights, no .NET runtime to install — while keeping startup fast, secrets safe and the door
open to the Microsoft Store later. Each decision lists the reason, so it can be bent when a project's constraints
differ. Details and copy-ready files are in `references/`:

| File | Read when |
|---|---|
| [references/project-setup.md](references/project-setup.md) | Creating the solution: layout, `Directory.Build.props`, csproj, publish profile, `app.manifest`, `global.json` |
| [references/runtime.md](references/runtime.md) | Data paths, portable mode, settings, logging, startup sequence, crash handler, secrets (DPAPI), launching other programs |
| [references/wpf-ui.md](references/wpf-ui.md) | Theme, colour tokens, custom title bar, message box, dialogs, keyboard, view-source tests |
| [references/ci-release-docs.md](references/ci-release-docs.md) | GitHub Actions CI and release, versioning, Store readiness, docs site, CI screenshots, spec/ADR process |

## Core decisions (the defaults)

1. **WPF on the current .NET LTS, built-in Fluent theme, MVVM with CommunityToolkit.Mvvm, no DI container.**
   WPF starts fast, virtualizes large lists, runs self-contained without installing anything, and *builds on
   Linux/macOS* with `EnableWindowsTargeting`. WinUI 3 unpackaged needs the Windows App SDK runtime (admin MSI) or
   extra ~40 MB and slower start, and its XAML compiler only runs on Windows. Avalonia only if cross-platform is a
   real requirement. A hand-written composition root keeps startup lean; the toolkit's source generators avoid
   reflection.
2. **Ship one self-contained, single-file, ReadyToRun win-x64 exe; compression off.** Native AOT and trimming do
   not work for WPF, so ReadyToRun + tiered PGO is the startup lever. Single-file compression halves the size but
   decompresses on *every* start — zip the release instead. Optionally also ship a small framework-dependent zip
   for machines that already have the Desktop Runtime. Expect ~150–170 MB exe (~70 MB zipped); that is the floor.
3. **Never elevate, never write outside the user profile.** Manifest `asInvoker`, `PerMonitorV2` DPI,
   `longPathAware`. Data in `%LOCALAPPDATA%\<App>`; no registry writes, services or machine-wide state. This is
   also exactly what keeps MSIX/Store packaging possible later without architecture changes.
4. **Portable mode by marker file.** A `portable.txt` next to the exe moves all data to `.\data`. One switch,
   checked once at startup; tools and test harnesses use it to stay away from real user data.
5. **Split OS-neutral from Windows code.** `App` (WPF, `net10.0-windows`) → `Windows` (P/Invoke, DPAPI, OS APIs,
   `net10.0-windows`) → `Core` / `Data` (logic, storage, crypto, `net10.0`). Everything testable runs on any OS in
   CI; only the thin WPF/DPAPI layer needs a Windows runner. Keep logic out of the `App` project.
6. **Startup: window first, everything else after.** Apply the theme, build the view model, `Show()` — then do
   sign-in, network, sync, and database work off the UI thread. Render from local data before any network call.
   Target: window visible in < 1 s.
7. **Secrets never in plaintext on disk, command lines or logs.** Protect local secrets with DPAPI
   (CurrentUser, optionally + LocalMachine to bind to the machine); for many secrets use an envelope: one random
   AES-256-GCM key wrapped by DPAPI, each secret encrypted with that key and its row id as associated data. Hand
   passwords to other programs through Windows Credential Manager, not arguments or files. A copied data folder
   then cannot be decrypted elsewhere — by design.
8. **Built-in Fluent theme + a small set of own tokens.** `Application.ThemeMode` (System/Light/Dark, suppress
   `WPF0001`) for neutrals and controls; own semantic colours (error, success, status…) in `Colors.Light.xaml` /
   `Colors.Dark.xaml` with identical keys, used via `DynamicResource`; no fixed colours in views. A third-party
   Fluent library (e.g. WPF UI) was tried and reverted: near-identical look, extra dependency, async dialog plumbing.
9. **Own title bar via `WindowChrome`, own message box.** A 32 px themed title bar that can hold content, with the
   OS still doing drag/snap/resize/shadow. An own `MessageWindow` so buttons name the action and risky
   confirmations default to **Cancel**. See the pitfalls in `references/wpf-ui.md` (snap layouts, doubled caption
   buttons).
10. **Keyboard-first, enforced by tests.** Every action has a shortcut shown next to it or in its tooltip;
    dialogs take Ctrl+S / Esc. Cheap XAML "view-source" tests catch bugs that otherwise only show in the running app.
11. **Warnings are errors, versions are central, SDK is pinned.** `TreatWarningsAsErrors`,
    `AnalysisLevel=latest-recommended`, `Directory.Packages.props`, `global.json`.
12. **CI on `windows-latest`; release from a `v*` tag.** Build, test, publish the exe as an artifact on every
    push/PR; the tag sets `-p:Version`, produces zips + SHA-256 sums and a GitHub Release. The app shows the same
    version (from `AssemblyInformationalVersion`).
13. **Spec + ADRs as the source of truth; docs site; real screenshots from CI.** Stable requirement IDs referenced
    from code/tests, one ADR per significant decision (superseded, never edited), a Starlight docs site, and
    screenshots captured from the real app on a Windows runner instead of hand-maintained mockups.

## How to apply

**Starting a new app:** create the layout and files from `references/project-setup.md`, then the runtime pieces
from `references/runtime.md` (paths, settings, log, startup). Run `dotnet build` on Linux to confirm the
cross-OS build works before writing UI. Add CI from `references/ci-release-docs.md` early so the publish profile is
exercised on Windows from day one. If the repo uses spec/ADRs, record decisions 1–4 and 7 as the first ADRs.

**Reviewing an existing app**, check in this order and report gaps:

- [ ] Manifest is `asInvoker`; nothing writes to Program Files, HKLM, or outside `%LOCALAPPDATA%`/temp.
- [ ] Publish is self-contained single-file ReadyToRun, compression off, `IncludeNativeLibrariesForSelfExtract`.
- [ ] No Windows API in the OS-neutral projects; those projects' tests run on Linux.
- [ ] `OnStartup` shows the window before any I/O beyond reading settings; long work is async/off-thread.
- [ ] Large lists are virtualized (also when grouped).
- [ ] No plaintext secret in settings, logs, temp files, `Process.Start` arguments; settings writes are atomic.
- [ ] No hard-coded colours in views; both colour dictionaries have the same keys.
- [ ] Unhandled-exception handler logs and shows a dialog with a fallback to the system `MessageBox`.
- [ ] Destructive confirmations default to Cancel.
- [ ] CI builds/tests/publishes on Windows; release version flows from the tag into the exe.

**When deviating** (e.g. an installer is acceptable, or cross-platform is required), revisit decisions 1–3 first —
most of the rest follows from "no install, no admin, fast start".
