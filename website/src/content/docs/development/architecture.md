---
title: Architecture
description: Projects, layers and the main runtime flows.
---

## Layers

```
 Harbor.App      WPF app (WPF UI Fluent controls, MVVM via CommunityToolkit.Mvvm), AppHost, settings
     │
     ├── Harbor.Windows   DPAPI / DPAPI-NG, Credential Manager, mstsc launcher, Windows & Entra identity
     │
     ├── Harbor.Data      SqlHarborStore (portable SQL, Dapper) + IDatabaseProvider (Sqlite, SqlServer)
     │
     └── Harbor.Core      ConnectionEntry, SecretCipher, VaultKeyManager, HarborWorkspace,
                          SessionPresenceService, RdpFile — OS-neutral (net10.0)
```

- `Harbor.Core` and `Harbor.Data` are **OS-neutral** (`net10.0`) and fully tested on any OS.
- Everything that needs Windows APIs lives in `Harbor.Windows`.
- There is **no DI container**: `AppHost` wires things by hand to keep startup lean.

## Key components

| Component | Responsibility |
|---|---|
| `HarborWorkspace` | Single entry point to entries. Local mode: one SQLite store. Remote mode: shared store as source of truth + local SQLite cache for reads and offline use. |
| `SqlHarborStore` | Implements `IConnectionStore` and `IPresenceStore` with portable SQL only. |
| `IDatabaseProvider` | Everything engine-specific: connection factory, clock expression, DDL, migrations. |
| `SecretCipher` | AES-256-GCM envelope with the entry id as associated data. |
| `VaultKeyManager` | Opens or (race-safely) creates a store's data-encryption key. |
| `IKeyProtector` | Wraps the DEK: `dpapi-user+machine`, `dpapi-user`, `dpapi-ng`, `passphrase`. |
| `SessionPresenceService` / `PresenceLease` | Presence records with heartbeat and TTL. |
| `RdpFile` / `MstscLauncher` | Build `.rdp` files, hand the password to Credential Manager, start `mstsc.exe`. |
| `IIdentityProvider` | Windows account or Entra ID (MSAL + WAM). |

## Startup (performance first)

1. The main window is shown immediately.
2. On a background thread: sign in, open the workspace (local SQLite first), sync if remote.
3. The list is rendered from the local file before any network call; the list is virtualized, also when grouped.

Target: main window visible in under 1 s; smooth with 10 000 entries.

## Remote mode data flow

- **Reads** always come from the local cache.
- **Writes** go to the shared database first; the stored row (with server-assigned version and timestamps) is then
  applied to the cache.
- **Sync** pulls rows changed since the watermark (minus a 5-minute overlap); deletes are tombstones.
- **Offline**: any connectivity failure switches to read-only cache mode; every sync tick retries.

## Technology choices

Each choice is recorded as an ADR in [`docs/adr/`](https://github.com/BartK1990/remote-desktop-harbor/tree/master/docs/adr):

| ADR | Decision |
|---|---|
| 0002 | WPF on .NET 10 |
| 0003 | Portable self-contained win-x64 exe, no install |
| 0004 | ADO.NET + Dapper with pluggable SQL providers |
| 0005 | AES-GCM envelope, DEK bound to user + machine with DPAPI |
| 0006 | Shared vault key: DPAPI-NG to an AD group, passphrase fallback |
| 0007 | Remote store as source of truth with local read-only offline cache |
| 0008 | Launch mstsc.exe, hand credentials over via Credential Manager |
| 0009 | Windows account by default, Entra ID via WAM broker |
| 0010 | Session presence: heartbeat records with server-clock TTL |
| 0011 | CI and release on GitHub Actions, win-x64 |
| 0012 | Microsoft Store readiness via MSIX packaging later |
| 0013 | Documentation site with Astro Starlight on GitHub Pages |
