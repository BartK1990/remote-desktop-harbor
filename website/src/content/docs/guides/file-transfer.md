---
title: File transfer
description: Copy files to and from your servers with transfers that survive dropped connections and restarts.
---

Harbor can copy files to and from the host of an entry. Large copies over a VPN often break; Harbor's transfers
**continue where they stopped** instead of starting over.

## Opening File transfer

Select an entry with a host and press <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>T</kbd>, or use **More → Transfer files…**
(also in the tree's right-click menu). The window shows the entry, its host and the account it uses — the account
selected in the tree ([several accounts](../managing-entries/)).

**Folder on the host** is a shared folder on the host as a UNC path. It starts as
`\\<host>\C$\Users\Public\Downloads`; type another one (any share, e.g. `\\<host>\Transfer` or `\\fileserver\drop`)
and Harbor remembers it for next time. The administrative share `C$` needs an administrator account on the host.

| Button | What it does |
|---|---|
| **Upload files…** | Pick files on this PC; they are copied into the folder on the host. |
| **Upload folder…** | Pick a folder; it is copied with its sub-folders. |
| **Download…** | Opens the folder on the host to pick files, then asks for a folder on this PC. |

Harbor connects the share with the entry's user name and password through Windows — the password is never put on a
command line or in a file. An entry without a user name connects as your Windows account. If you are already
connected to that server as another user, that connection is used.

An existing file is not overwritten by surprise: a download asks before replacing local files, and an upload whose
file already exists on the host fails with a message.

## Following transfers

Transfers run one after another. Each row shows the file, the direction, the destination, a progress bar and the
state:

| State | Meaning |
|---|---|
| Waiting | Queued behind other transfers. |
| Copying | Copying now, with bytes done of the total. |
| Connection lost, retrying | The connection dropped; Harbor retries automatically from where it stopped. |
| Paused | Paused by you, by closing the app, or after 10 minutes of retrying. **Resume** continues where it stopped. |
| Done | Copied completely; the file has its final name. |
| Failed | A problem retrying cannot fix (source gone, file exists, access denied). **Resume** tries again. |

**Pause** and **Resume** work on one transfer, **Resume all** on every paused one. **Cancel** stops a transfer and
deletes the partly copied file; on a finished transfer the same button **Remove**s it from the list (the file
stays). **Clear done** removes all finished rows. The window can be closed while transfers run.

## How resuming works

- Each file is copied in 4 MB chunks into `<name>.harbor-part` next to its destination.
- After every chunk is written and flushed, Harbor saves its position and a hash in a small journal
  (`transfers\<id>.json` in the [data folder](../../reference/data-and-files/)). So Harbor always knows exactly how
  much reached the destination — after a dropped connection, a closed app or a crash.
- Before continuing it checks the partial file against the journal (its length and the last chunk's hash) and copies
  again anything that did not really arrive. If the source file changed in the meantime the copy starts over.
- When all bytes are in, the partial file is renamed to its real name.

Closing Harbor while transfers are unfinished asks first; they come back **paused** at the next start and continue
from where they stopped when you press **Resume**. The journal holds no password.
