---
title: Installation
description: Download Remote Desktop Harbor and run it without installation or administrator rights.
---

Remote Desktop Harbor is distributed as a portable zip. There is no installer and you do not need administrator
rights.

## Requirements

| | |
|---|---|
| Operating system | Windows 10 version 1809 or later, or Windows 11 |
| Architecture | x64 |
| Rights | A standard (non-admin) Windows account is enough |
| Runtime | None for the default build; the `-requires-dotnet10` build needs the .NET 10 Desktop Runtime |

## Download

Get the latest release from the
[GitHub Releases page](https://github.com/BartK1990/remote-desktop-harbor/releases). Each release contains:

| File | When to use it |
|---|---|
| `RemoteDesktopHarbor-<version>-win-x64.zip` | **Recommended.** Self-contained — includes the .NET runtime, nothing else to install. |
| `RemoteDesktopHarbor-<version>-win-x64-requires-dotnet10.zip` | About 10× smaller. Only for machines that already have the **.NET 10 Desktop Runtime**. |
| `SHA256SUMS.txt` | SHA-256 checksums of both zips. |

To verify a download in PowerShell:

```powershell
Get-FileHash .\RemoteDesktopHarbor-0.1.0-win-x64.zip -Algorithm SHA256
```

Compare the hash with the matching line in `SHA256SUMS.txt`.

## Run

1. Unzip the archive to any folder you can write to (for example `C:\Tools\RemoteDesktopHarbor`).
2. Double-click `RemoteDesktopHarbor.exe`.

On first start the app creates its data folder at `%LOCALAPPDATA%\RemoteDesktopHarbor` and a new, empty local vault.
See [Data and files](../../reference/data-and-files/) for everything it stores there.

:::tip
Want to keep the data next to the exe, for example on a USB stick or a synced folder? Use
[portable mode](../portable-mode/).
:::

:::note[SmartScreen]
The exe is not code-signed yet, so Windows SmartScreen may warn on first start. Choose **More info → Run anyway**
after verifying the checksum.
:::

## Update

Updates are “replace the exe”: close the app, unzip the new version over the old folder and start it again.
Your data lives in the data folder, not next to the exe (unless you use portable mode), so it is kept.
Database schema upgrades are applied automatically.

## Uninstall

Delete the app folder. To also remove your data, delete `%LOCALAPPDATA%\RemoteDesktopHarbor`
(or the `data` folder next to the exe in portable mode).
