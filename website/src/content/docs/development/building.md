---
title: Building from source
description: Build, test and publish Remote Desktop Harbor.
---

## Prerequisites

- **.NET 10 SDK** (the exact version is pinned in `global.json`).
- Any OS for building and testing. The WPF app builds everywhere thanks to `EnableWindowsTargeting`, but only runs on
  Windows.

## Build and test

```bash
dotnet build RemoteDesktopHarbor.slnx
```

```bash
dotnet test RemoteDesktopHarbor.slnx
```

Warnings are treated as errors. The tests (xUnit) cover the core logic, crypto and storage against real SQLite, and
run on Linux, macOS and Windows.

## Publish the exe

```bash
dotnet publish src/Harbor.App/Harbor.App.csproj -p:PublishProfile=win-x64
```

Output: `artifacts/publish/win-x64/RemoteDesktopHarbor.exe` — a self-contained, single-file, ReadyToRun build.
Single-file compression is deliberately off: it would halve the size but slow down every start.

For a small framework-dependent build (needs the .NET 10 Desktop Runtime):

```bash
dotnet publish src/Harbor.App/Harbor.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:PublishReadyToRun=true
```

## Repository layout

| Path | Content |
|---|---|
| `docs/spec/SPEC.md` | Requirements (FR/NFR IDs), data and security model — **start here** |
| `docs/adr/` | Architecture decision records |
| `src/Harbor.Core` | Domain, crypto envelope, workspace (sync/offline), presence, `.rdp` handling — OS-neutral |
| `src/Harbor.Data` | ADO.NET/Dapper store + SQL providers (SQLite, SQL Server) |
| `src/Harbor.Windows` | DPAPI / DPAPI-NG, Credential Manager, mstsc launcher, Windows & Entra identity |
| `src/Harbor.App` | WPF UI (MVVM), composition root, settings |
| `tests/` | xUnit tests |
| `website/` | This documentation site (Astro Starlight) |

Package versions are managed centrally in `Directory.Packages.props`.

## Documentation site

The site lives in `website/` and needs Node.js 22+:

```bash
cd website && npm ci && npm run dev
```

Pages are Markdown files under `website/src/content/docs/`; the sidebar is defined in `website/astro.config.mjs`.
The site is deployed to GitHub Pages on every push to `master` that touches `website/`.
