---
title: Settings
description: Every setting in the Settings dialog and settings.json.
---

Open **Settings** from the toolbar. After **Save**, the app offers to restart, because storage and sign-in changes
take effect at startup.

![The Settings dialog with its Storage, Sign-in, Remote Desktop and Data folder sections.](../../../assets/screenshots/settings.png)

Settings are stored as JSON in `settings.json` in the [data folder](../data-and-files/). The remote connection string is
DPAPI-protected for your Windows account, so a copied `settings.json` does not reveal it.

## Storage

| UI | JSON property | Default | Description |
|---|---|---|---|
| Local vault / Shared database | `StorageMode` | `Local` | `Local` — SQLite file on this computer. `Remote` — shared database with offline copy. |
| Provider | `RemoteProvider` | `SqlServer` | Registered database provider for the shared database. |
| Connection string | `RemoteConnectionStringProtected` | — | Connection string, DPAPI-protected (base64). Edit it in the UI only. |
| Shared vault key protection | `RemoteKeyScheme` | `dpapi-ng` | `dpapi-ng` (AD group) or `passphrase`. Only used when the shared database has no vault key yet. |
| (descriptor box) | `RemoteKeyDescriptor` | — | DPAPI-NG protection descriptor, e.g. `SID=S-1-5-21-…`. Required for `dpapi-ng`. |
| Create / upgrade schema automatically | `RemoteAutoMigrate` | `true` | Apply schema migrations at startup. Needs DDL rights. |
| Sync interval (seconds) | `RemoteSyncIntervalSeconds` | `60` | How often the local copy is refreshed. Minimum 10. |
| Session presence timeout | `PresenceTtlSeconds` | `120` | Lifetime of a presence record without heartbeat. |
| Session presence heartbeat | `PresenceHeartbeatSeconds` | `45` | Refresh interval; must be ≤ half the timeout. |

## Sign-in

| UI | JSON property | Default | Description |
|---|---|---|---|
| Windows account / Microsoft Entra ID | `AuthMode` | `Windows` | `Windows` or `EntraId`. |
| Application (client) ID | `EntraClientId` | — | Required for Entra ID. See [Entra ID sign-in](../../team/entra-id/). |
| Tenant ID or domain | `EntraTenantId` | `organizations` | Tenant ID, domain, or `organizations`. |

## Remote Desktop

| UI | JSON property | Default | Description |
|---|---|---|---|
| Keep the password in Windows Credential Manager after connecting | `KeepCredentialAfterLaunch` | `false` | Off: credential removed ~30 s after launch and never survives logoff. On: kept, so reconnects work without the app. |

## Data folder

Read-only. Shows where the app stores its data, with “(portable mode)” when [portable mode](../../getting-started/portable-mode/)
is active.

## Remembered by the main window

| UI | JSON property | Default | Description |
|---|---|---|---|
| Folder arrows in the entry tree | `CollapsedFolders` | `[]` | Folders collapsed in the entry tree, restored at start. Written when the app closes. |

## Validation

The dialog refuses to save when:

- shared storage is selected without a connection string,
- AD-group protection is selected without a descriptor,
- Entra ID is selected without a client ID,
- the heartbeat is more than half of the presence timeout.

## Example `settings.json`

```json
{
  "StorageMode": "Remote",
  "RemoteProvider": "SqlServer",
  "RemoteConnectionStringProtected": "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA…",
  "RemoteKeyScheme": "dpapi-ng",
  "RemoteKeyDescriptor": "SID=S-1-5-21-1004336348-1177238915-682003330-5126",
  "RemoteAutoMigrate": false,
  "RemoteSyncIntervalSeconds": 60,
  "AuthMode": "Windows",
  "EntraClientId": null,
  "EntraTenantId": "organizations",
  "PresenceTtlSeconds": 120,
  "PresenceHeartbeatSeconds": 45,
  "KeepCredentialAfterLaunch": false,
  "CollapsedFolders": [ "Lab" ]
}
```
