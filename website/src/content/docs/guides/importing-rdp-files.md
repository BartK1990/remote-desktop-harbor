---
title: Importing .rdp files
description: Turn existing Remote Desktop (.rdp) files into catalog entries.
---

If you already have `.rdp` files saved from the Remote Desktop client, you can import them instead of retyping.

## Import

1. Click **Import .rdp…** in the toolbar (or in an open entry editor).
2. Pick the `.rdp` file.
3. Review the pre-filled editor, add a password if you want one stored, and click **Save**.

## What is imported

| `.rdp` line | Becomes |
|---|---|
| `full address:s:host[:port]` | **Host** and **Port** (IPv6 literals in brackets are supported) |
| `server port:i:…` | **Port** |
| `username:s:…` | **User name**. `DOMAIN\user` is split into **Domain** and **User name** if no `domain` line exists. |
| `domain:s:…` | **Domain** |
| `password 51:b:…`, `password:…` | **Discarded.** Saved passwords in `.rdp` files are never imported. |
| display, audio, redirection and connection settings the editor knows (e.g. `screen mode id`, `desktopwidth`, `audiomode`) | The matching [RDP settings](../managing-entries/#rdp-settings) fields |
| everything else | Kept as-is in **Other .rdp settings** |

:::caution
Delete the original `.rdp` file afterwards if it contained a saved password — Harbor does not touch the source file.
:::

## RDP settings format

Entries store their settings as ordinary `.rdp` lines. The editor shows the common ones as
[fields](../managing-entries/#rdp-settings); the **Other .rdp settings** box holds the rest, one per line:

```ini
gatewayhostname:s:gw.contoso.com
gatewayusagemethod:i:1
drivestoredirect:s:C:\;D:\;
```

A line for a setting that also has a field overrides the field.

The keys `full address`, `server port`, `username`, `domain` and any password keys are managed by the app and are
ignored if you type them here — edit the corresponding fields instead.

For the list of supported keys, see Microsoft's
[supported RDP properties](https://learn.microsoft.com/windows-server/remote/remote-desktop-services/clients/rdp-files).
