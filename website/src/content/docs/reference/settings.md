---
title: Settings
description: Every setting in the Settings dialog and settings.json.
---

Open **Settings** with the gear button at the right of the toolbar. The sections are listed on the left:
**Storage**, **Shared vault**, **Sign-in**, **Remote Desktop**, **Appearance** and **About**. **Save**
(<kbd>Ctrl</kbd>+<kbd>S</kbd>) stores the settings of all sections; a validation error is shown at the bottom
whichever section is open, and **Cancel** (<kbd>Esc</kbd>) discards everything. After saving, the app offers to
restart when something besides the theme changed, because storage and sign-in changes take effect at startup.

Settings are stored as JSON in `settings.json` in the [data folder](../data-and-files/). The remote connection string is
DPAPI-protected for your Windows account, so a copied `settings.json` does not reveal it.

## Storage

![Settings, Storage: the Shared database card selected; server sql01, database Harbor, SQL Server login, Encrypt on, the masked connection string preview and a successful Test connection.](../../../assets/screenshots/settings.png)

| UI | JSON property | Default | Description |
|---|---|---|---|
| **Local vault** / **Shared database** cards | `StorageMode` | `Local` | `Local` — SQLite file on this computer. `Remote` — shared database with offline copy. |
| Provider | `RemoteProvider` | `SqlServer` | Registered database provider for the shared database. |
| Server, Database name, Database sign-in, User name, Password, Encrypt, Trust server certificate | `RemoteConnectionStringProtected` | — | Built into a connection string, DPAPI-protected (base64). Edit it in the UI only. |
| Edit as connection string (advanced) | (same) | off | Edit the raw connection string instead of the fields. Strings the fields cannot describe open here automatically. |
| Connection string preview | (read-only) | — | The connection string the fields produce — what Test and Save use — with the password shown as `********`. |
| Create the database if it does not exist | — (not stored) | off | On **Test connection** and **Save**, create the database if missing. Needs `CREATE DATABASE` rights. |
| Test connection | — | — | Connects with the current fields; the result is shown in green (OK) or red (failed). |

## Shared vault

Used with a shared database only; disabled while *Local vault* is chosen.

![Settings, Shared vault: vault key protection, schema auto-upgrade, sync interval and presence timings.](../../../assets/screenshots/settings-vault.png)

| UI | JSON property | Default | Description |
|---|---|---|---|
| Vault key protection | `RemoteKeyScheme` | `sign-in` | `sign-in` (*Not protected*: key stored unencrypted in the database), `dpapi-ng` (AD group) or `passphrase`. Used when the shared database has no vault key yet. |
| Convert the existing shared vault to this protection | `RemoteKeyConvert` | `false` | One-time: on the next start, re-protect the existing vault key with the chosen protection; cleared afterwards. See [Vault key protection](../../team/vault-key-protection/). |
| (descriptor box) | `RemoteKeyDescriptor` | — | DPAPI-NG protection descriptor, e.g. `SID=S-1-5-21-…`. Required for `dpapi-ng`. |
| Create / upgrade schema automatically | `RemoteAutoMigrate` | `true` | Apply schema migrations at startup. Needs DDL rights. |
| Sync interval (s) | `RemoteSyncIntervalSeconds` | `60` | How often the local copy is refreshed. Minimum 10. |
| Presence timeout (s) | `PresenceTtlSeconds` | `120` | Lifetime of a presence record without heartbeat. |
| Presence heartbeat (s) | `PresenceHeartbeatSeconds` | `45` | Refresh interval; must be ≤ half the timeout. |

## Sign-in

![Settings, Sign-in with “No sign-in” and a name.](../../../assets/screenshots/settings-signin.png)

| UI | JSON property | Default | Description |
|---|---|---|---|
| Windows account / Microsoft Entra ID / No sign-in | `AuthMode` | `Windows` | `Windows` (recommended), `EntraId` or `Anonymous`. Who you are for presence and audit; independent of storage and of the database sign-in. |
| Application (client) ID | `EntraClientId` | — | Required for Entra ID. See [Entra ID sign-in](../../team/entra-id/). |
| Tenant ID or domain | `EntraTenantId` | `organizations` | Tenant ID, domain, or `organizations`. |
| Your name | `AnonymousName` | — | Required for *No sign-in*. Shown to others as “*name* (not signed in)” and in “Last changed by”. Not verified. |

## Remote Desktop

| UI | JSON property | Default | Description |
|---|---|---|---|
| Keep the password in Windows Credential Manager after connecting | `KeepCredentialAfterLaunch` | `false` | Off: credential removed ~30 s after launch and never survives logoff. On: kept, so reconnects work without the app. |
| Ask before connecting to a production (PROD) entry | `ConfirmProductionConnect` | `true` | Connecting to an entry in a *Production*/*Prod*/*PRD*/*Live* folder asks first. See [Production entries](../../guides/connecting/#production-entries). |

## Appearance

![Settings, Appearance: the theme choice System, Light and Dark.](../../../assets/screenshots/settings-appearance.png)

| UI | JSON property | Default | Description |
|---|---|---|---|
| Theme: System / Light / Dark | `Theme` | `System` | *System* follows the Windows light/dark setting, also when it changes. The theme changes as soon as you pick it; **Save** keeps it without a restart, **Cancel** goes back. |

## About

Read-only. The app version and, when known, the build commit, and the data folder — where the app stores its data,
with “(portable mode)” when [portable mode](../../getting-started/portable-mode/) is active. **Open** shows it in
Explorer.

## Remembered by the main window

| UI | JSON property | Default | Description |
|---|---|---|---|
| Folder arrows in the entry tree | `CollapsedFolders` | `[]` | Folders collapsed in the entry tree, restored at start. Written when the app closes. |
| Pin (star, <kbd>Ctrl</kbd>+<kbd>D</kbd>) | `PinnedEntryIds` | `[]` | Ids of the entries you pinned. Personal: never written to the shared database. |
| (connecting) | `RecentEntryIds` | `[]` | Ids of the last 10 entries you connected to, most recent first. Personal. |

## Validation

The dialog refuses to save when:

- shared storage is selected without a server, database name or (for a SQL login) user name — or, when editing the
  raw string, without a connection string,
- the database should be created but that fails (untick the option to save anyway),
- AD-group protection is selected without a descriptor,
- Entra ID is selected without a client ID,
- *No sign-in* is selected without a name,
- the heartbeat is more than half of the presence timeout.

## Example `settings.json`

```json
{
  "StorageMode": "Remote",
  "RemoteProvider": "SqlServer",
  "RemoteConnectionStringProtected": "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA…",
  "RemoteKeyScheme": "dpapi-ng",
  "RemoteKeyConvert": false,
  "RemoteKeyDescriptor": "SID=S-1-5-21-1004336348-1177238915-682003330-5126",
  "RemoteAutoMigrate": false,
  "RemoteSyncIntervalSeconds": 60,
  "AuthMode": "Windows",
  "EntraClientId": null,
  "EntraTenantId": "organizations",
  "AnonymousName": null,
  "PresenceTtlSeconds": 120,
  "PresenceHeartbeatSeconds": 45,
  "KeepCredentialAfterLaunch": false,
  "ConfirmProductionConnect": true,
  "Theme": "System",
  "CollapsedFolders": [ "Lab" ],
  "PinnedEntryIds": [ "5b0c3f0e-2f63-4a5e-9a4f-1f0c2a8e7d11" ],
  "RecentEntryIds": [ "5b0c3f0e-2f63-4a5e-9a4f-1f0c2a8e7d11", "c2a1f1d4-8e0b-4f7e-b7a1-3b6d2c9e4a20" ]
}
```
