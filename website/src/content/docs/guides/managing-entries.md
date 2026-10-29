---
title: Managing entries
description: Create, edit, organise, search, copy and delete connection entries.
---

Every item in the catalog is an **entry**. The main window shows the entries on the left in a compact folder tree,
and the details of the selected entry on the right.

![Main window: search box and toolbar at the top, compact folder tree of entries on the left, details and actions of the selected entry on the right, status bar at the bottom.](../../../assets/screenshots/main.png)

## Entry kinds

| Kind | Stores | Connecting uses |
|---|---|---|
| **RDP connection** | Host, port, credentials **and** additional `.rdp` settings | Default settings + the entry's own `.rdp` settings |
| **Credentials only** | User name and password, optional host | Default settings only |

Pick the kind at the top of the editor. You can switch it later; the `.rdp` settings box is only shown for
RDP connections.

## Fields

![The entry editor for an RDP connection.](../../../assets/screenshots/editor.png)

| Field | Notes |
|---|---|
| **Name** | Required. Shown in the list. |
| **Folder** | Optional. Type a new name or pick an existing one. Use `/` (or `\`) for sub-folders, e.g. `Production/Web`. |
| **Host** / **Port** | Host name or IP address (IPv6 literals like `[fe80::1]` work). Port defaults to `3389`. An entry without a host cannot be launched, but you can still copy its credentials. |
| **Domain** / **User name** | Combined as `DOMAIN\user`. If the user name already contains `\` or `@` (UPN), the domain is not added. |
| **Password** | Optional. Encrypted before it is written to any database. When editing an entry that already has a password, tick **Change stored password** to replace or clear it. |
| **RDP settings** | RDP connections only. Raw `.rdp` lines in `key:type:value` format, e.g. `screen mode id:i:1`. |
| **Notes** | Free text. Searchable. |

The details pane also shows who changed the entry last and when.

## The folder tree

Each folder and entry is one low row showing just its name; folders also show how many entries they contain, and
entries in use show an **In use by …** badge. Hover an entry for its user and host.

- Click the arrow next to a folder, double-click it, or press <kbd>←</kbd>/<kbd>→</kbd> to collapse or expand it.
- Right-click the tree for **Expand all** and **Collapse all** (and the actions for the selected entry).
- Folders are matched regardless of case; entries without a folder are listed at the root, after the folders.
- Collapsed folders are remembered the next time you start the app.

## Create

Click **New** (<kbd>Ctrl</kbd>+<kbd>N</kbd>). The new entry starts in the selected folder, or in the folder of the
selected entry.

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

Type in the search box (<kbd>Ctrl</kbd>+<kbd>F</kbd>). The tree is filtered as you type, by **name, host, user,
folder and notes**, and every folder with matches is expanded; the status bar shows how many entries match.
<kbd>Esc</kbd> clears the search, <kbd>↓</kbd> moves into the tree. Filtering happens in memory, so it stays
instant with thousands of entries.

## Copy user name and password

- **Copy user** copies the qualified user name (`DOMAIN\user`).
- **Copy password** (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>C</kbd>) decrypts the password and puts it on the
  clipboard. After **30 seconds** the clipboard is cleared — but only if it still contains that password, so
  anything you copied in between is left alone.

Copying works offline too, and is the fallback when a policy prevents Remote Desktop from using saved credentials.

## Read-only while offline

When the app uses a shared database that is currently unreachable, **New**, **Import .rdp…**, **Edit** and
**Delete** are disabled. See [Offline mode](../../team/offline-mode/).

## Light or dark theme

The theme box at the right of the toolbar switches the app between **System** (follows the Windows light/dark
setting, the default), **Light** and **Dark**. The change applies immediately and is remembered for the next start.
