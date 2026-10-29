---
title: Data and files
description: Where the app stores its settings, databases, logs and temporary files.
---

## Data folder

| Mode | Location |
|---|---|
| Default | `%LOCALAPPDATA%\RemoteDesktopHarbor` |
| [Portable mode](../../getting-started/portable-mode/) | `data\` next to `RemoteDesktopHarbor.exe` |

Nothing is written outside the data folder and your user's temp directory — no registry keys, no services, no
machine-wide files.

## Contents

| Path | Content |
|---|---|
| `settings.json` | [Settings](../settings/). The remote connection string is DPAPI-protected. |
| `harbor.db` | Local vault (SQLite) — used in local storage mode. |
| `cache-<hash>.db` | Local offline copy of a shared database. One file per provider + connection string, so switching databases never mixes data. |
| `logs\harbor.log` | Application log. Rotated to `harbor.log.1` at 2 MB. Never contains secrets. |
| `tmp\*.rdp` | Temporary launch files (no passwords). Deleted ~30 s after launch; leftovers older than 5 minutes are removed at startup. |

All SQLite files contain only encrypted passwords plus a vault key wrapped for your Windows user and this machine.

## Windows Credential Manager

While a session is launching, the app stores a credential named `TERMSRV/<host>` in your Windows Credential Manager.
It is removed ~30 s later unless **Keep the password in Windows Credential Manager** is enabled. See
[Connecting](../../guides/connecting/).

## Backups

- **Local vault:** the passwords in `harbor.db` can only be decrypted by the same Windows user on the same machine.
  A backup restored to another machine (or after a Windows profile reset) cannot decrypt them. Keep an independent
  backup of important passwords, or use a [shared database](../../team/shared-database/).
- **Shared database:** back up the SQL database as usual. The cache files can always be rebuilt and need no backup.
