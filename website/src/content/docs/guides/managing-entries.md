---
title: Managing entries
description: Create, edit, organise, search, copy and delete connection entries.
---

Every item in the catalog is an **entry**. The main window shows the entries on the left, grouped by folder, and the
details of the selected entry on the right.

## Entry kinds

| Kind | Stores | Connecting uses |
|---|---|---|
| **RDP connection** | Host, port, credentials **and** additional `.rdp` settings | Default settings + the entry's own `.rdp` settings |
| **Credentials only** | User name and password, optional host | Default settings only |

Pick the kind at the top of the editor. You can switch it later; the `.rdp` settings box is only shown for
RDP connections.

## Fields

| Field | Notes |
|---|---|
| **Name** | Required. Shown in the list. |
| **Folder** | Optional. Entries are grouped by folder; type a new name or pick an existing one. |
| **Host** / **Port** | Host name or IP address (IPv6 literals like `[fe80::1]` work). Port defaults to `3389`. An entry without a host cannot be launched, but you can still copy its credentials. |
| **Domain** / **User name** | Combined as `DOMAIN\user`. If the user name already contains `\` or `@` (UPN), the domain is not added. |
| **Password** | Optional. Encrypted before it is written to any database. When editing an entry that already has a password, tick **Change stored password** to replace or clear it. |
| **RDP settings** | RDP connections only. Raw `.rdp` lines in `key:type:value` format, e.g. `screen mode id:i:1`. |
| **Notes** | Free text. Searchable. |

The details pane also shows who changed the entry last and when.

## Create

Click **New** (<kbd>Ctrl</kbd>+<kbd>N</kbd>). The new entry starts in the folder of the currently selected entry.

## Edit

Select an entry and click **Edit**. If saving fails because of a validation or database error, the editor reopens
with your input and the error message.

:::note[Concurrent edits]
In a shared database, if someone else saved the same entry after you opened it, your save is rejected with
**Not saved** rather than silently overwriting their change. The app syncs automatically; open the entry again and
re-apply your edit.
:::

## Delete

Select an entry and click **Delete**, then confirm. In a shared database the entry is removed for everyone.

## Search

Type in the search box (<kbd>Ctrl</kbd>+<kbd>F</kbd>). The list is filtered as you type, by **name, host, user,
folder and notes**. Filtering happens in memory, so it stays instant with thousands of entries.

## Copy user name and password

- **Copy user** copies the qualified user name (`DOMAIN\user`).
- **Copy password** (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>C</kbd>) decrypts the password and puts it on the
  clipboard. After **30 seconds** the clipboard is cleared — but only if it still contains that password, so
  anything you copied in between is left alone.

Copying works offline too, and is the fallback when a policy prevents Remote Desktop from using saved credentials.

## Read-only while offline

When the app uses a shared database that is currently unreachable, **New**, **Import .rdp…**, **Edit** and
**Delete** are disabled. See [Offline mode](../../team/offline-mode/).
