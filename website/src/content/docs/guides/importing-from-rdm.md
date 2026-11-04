---
title: Importing from Remote Desktop Manager
description: Move your connections and credentials from Devolutions Remote Desktop Manager via its CSV export.
---

Harbor can import the CSV export of Devolutions **Remote Desktop Manager** (RDM), so you don't have to retype your
catalog.

## Export from Remote Desktop Manager

In RDM, select the entries or folders to move and choose **File → Export → Export entries (CSV)**. Include the
credentials if you want the passwords to come along.

## Import

1. Click **Import RDM…** in the toolbar.
2. Pick the `.csv` file.
3. Harbor shows how many entries it will add, how many are already in your catalog, and which entry types it skips.
   Click **Import** to add them.

Imported entries keep their RDM folders. In a shared database they are visible to everyone right away.

## What is imported

| RDM type | Becomes |
|---|---|
| **RDP (Microsoft Remote Desktop)** | RDP connection |
| **Username and password** | Credentials only |
| **Folder** | Not imported as an entry — folders are rebuilt from each entry's group. |
| anything else (VNC, TeamViewer, Website, Citrix, VPN, KeePass…) | Skipped and listed in the confirmation |

| CSV column | Becomes |
|---|---|
| `Name` | **Name** (the host when empty) |
| `Group` | **Folder** — `Production\Web` becomes the nested folder `Production/Web` |
| `Host`, `Port` | **Host** and **Port** (`host:port` in Host is split; default 3389) |
| `CredentialDomain`, `CredentialUserName` | **Domain** and **User name** (`DOMAIN\user` is split when there is no domain) |
| `CredentialPassword` | **Password**, encrypted before it is saved |
| `Description`, `Keywords` | **Notes** |
| `OpenInConsole` = `Yes` | **Administrative session** turned on |
| `WebUrl`, `URL`, `Expiration`, web credentials | Ignored |

Entries that already exist — same type, name, folder, host, port and user — are left out, so running the same import
twice adds nothing.

:::caution
The RDM CSV export contains your passwords in **plain text**. Delete the file once the import is done — Harbor never
modifies or keeps it.
:::
