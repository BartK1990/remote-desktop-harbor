---
title: Entries on this PC only
description: Keep some entries private on your PC while the rest of the catalog lives in the shared database, and move entries both ways.
---

In [shared mode](../shared-database/) every entry is shared with the team. Some entries are better kept to yourself —
a lab VM, a personal jump host, credentials you must not share. Harbor can keep those **on this PC only**, next to the
shared catalog.

## Creating a private entry

**New** (<kbd>Ctrl</kbd>+<kbd>N</kbd>) shows **Stored in** at the top of the **General** tab:

| Choice | Where the entry lives | Who sees it |
|---|---|---|
| **Shared database** (default) | The shared SQL database, plus every client's offline copy | Everyone using the shared database |
| **This PC only** | This PC's local vault (`harbor.db`, the same file local mode uses) | Only you, on this PC |

While the shared database is [offline](../offline-mode/), new entries are kept on this PC — you can move them to the
shared database later.

Private entries show a small PC mark next to their name in the tree and **This PC only** in the details pane. The
**This PC** filter above the tree lists only them — handy for spotting entries that are also in the shared database
(for example after switching from local mode and restoring or importing the same list there). To remove such
duplicates in one go, pick **This PC**, right-click the tree and choose **Delete shown entries…** — only the copies on
this PC are deleted; the action never deletes from the shared database. They
work like any other entry: connect, copy, pin, search. Two differences:

- They can be edited and deleted **also while the shared database is offline**.
- Connecting publishes **no [presence](../session-presence/)**: nobody else knows the entry exists. Your own open
  sessions still show as in use on your PC.

## Moving entries

Select an entry and use **More** (or right-click it in the tree):

- **Move to this PC only…** — removes the entry from the shared database for everyone and keeps it, with its accounts
  and passwords, in this PC's vault.
- **Move to shared database…** — shares a private entry with everyone using the shared database.

Both ask first (Cancel is the default) and need the shared database online. Passwords are re-encrypted for the
target vault; they are never shown or written in plain text. Pins and recent entries follow the moved entry.

If someone changed the entry in the shared database meanwhile, the move is refused and nothing changes — refresh
(<kbd>F5</kbd>) and try again.

## Security

Private entries are encrypted with this PC's vault key, which only your Windows account on this computer can unwrap
(the same protection as [local mode](../../reference/security-model/)). They never reach the shared database, its
backups or its administrators. The flip side: the shared database does not back them up — if you lose the PC or your
Windows profile, they are gone.
