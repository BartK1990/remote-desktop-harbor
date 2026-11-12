---
title: Backup and restore
description: Export your entries, passwords, pins and smart groups to one encrypted file and restore them on another PC.
---

The passwords in Harbor's local vault can only be decrypted by the same Windows user on the same PC, so copying
`harbor.db` to a new computer does not bring them along. Use a **backup file** instead: it holds your whole list and
is encrypted with a passphrase you choose, so any PC can restore it.

## Export a backup

1. Click **Backup → Export backup…** in the toolbar.
2. Choose where to save the `.harborbackup` file (e.g. a USB stick or your cloud drive).
3. Choose a **backup passphrase** — at least 12 characters, typed twice.

The backup contains every entry — name, folder, kind, host, port, domain, user name, notes, `.rdp` settings,
additional accounts and **all passwords** — plus your [pins](../managing-entries/) and
[smart groups](../smart-groups/). With a shared database it holds the shared entries and the ones kept on
[this PC only](../../team/local-entries/). Export also works while the shared database is offline.

The whole file is encrypted (AES-256-GCM, key derived from the passphrase with PBKDF2-SHA256): without the passphrase
nobody can read even the entry names.

:::caution
The passphrase cannot be recovered. Without it the backup cannot be restored. Keep it in a password manager or
somewhere else safe — not next to the file.
:::

## Restore on another PC

1. Install Harbor on the new PC and start it once.
2. Click **Backup → Restore backup…** and pick the `.harborbackup` file.
3. Enter the passphrase.
4. Harbor shows how many entries and smart groups it will add and how many are already there. Click **Restore**.

Restored passwords are encrypted again with the new PC's own key. Entries that are already in your catalog (same
kind, name, folder, host, port and user) are left out, so restoring the same backup twice adds nothing — but a pinned
entry in the backup also pins the one already there.

With a shared database, restored entries are added to the shared database for everyone, except the ones that were
kept on the old PC only: those go to this PC only again. Restoring needs the shared database to be online.

## What is not in a backup

- The list of recently used entries, and your other [settings](../../reference/settings/) (theme, list size, storage,
  sign-in).
- Who is connected where — that is live data of the shared database.
