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

**Copy over** chooses how files reach the server:

| Choice | Needs | How it works |
|---|---|---|
| **Remote Desktop (port 3389)** — default | Only what Remote Desktop needs | A helper started in the Remote Desktop session copies the files through your PC's drive shared into the session. |
| **Network share (port 445)** | Windows file sharing to the server | Harbor copies the files itself over a share such as `C$`. |

Not sure? Run `Test-NetConnection <server> -Port 445` in PowerShell: if `TcpTestSucceeded` is `False`, use
**Remote Desktop**.

## Copying through Remote Desktop

1. Type the **Folder on the server** as the server sees it — `%USERPROFILE%\Downloads` (the default), `D:\Install`…
   Environment variables are expanded on the server. Choose what happens to [files that already exist](#files-that-already-exist).
2. Click **Upload files…**, **Upload folder…** or **Download…** (for a download you pick the folder on this PC; the files
   themselves are picked on the server).
3. Harbor opens Remote Desktop for the entry with the needed drive of this PC shared into the session, and puts the
   start command on the clipboard. If a session is already open with that account, it is reconnected with the drive.
4. In the session press <kbd>Win</kbd>+<kbd>R</kbd>, paste with <kbd>Ctrl</kbd>+<kbd>V</kbd> and press
   <kbd>Enter</kbd>. If Windows asks whether to run the file, choose **Run**. A window opens on the server and copies the
   files; for a download it first asks which files to copy.

The command looks like `\\tsclient\C\Users\you\AppData\Local\RemoteDesktopHarbor\rdp-transfers\<id>\start.cmd`.
**Copy command** in the window (and on each unfinished row) puts it on the clipboard again.

The File transfer window shows the progress the helper reports: file *n* of *m*, bytes, *waiting for Remote Desktop to
reconnect*, done or failed. **Cancel** asks the helper to stop and delete the partly copied files.

**When the connection drops**, the remote session keeps running and so does the helper: it waits for Remote Desktop to
reconnect — by itself or when you connect again — and continues where it stopped. Its journal is kept on the server.
To resume a transfer whose window was closed, run the start command again.

:::note
While that session is connected, the shared drive of your PC (at least the drive of Harbor's data folder, usually
`C:`) is visible to the server — that is how the files get there. The server must allow drive redirection; if a policy
turns it off, the command reports that `\\tsclient` cannot be found. No password is written to any file or command.
:::

## Copying over a network share

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

Files that already exist are handled as described [below](#files-that-already-exist).

## Files that already exist

**Files that already exist** applies to both ways of copying:

| Choice | What happens |
| --- | --- |
| **Fail** (default) | A file that is already there is not copied and shows as failed; the other files are copied. |
| **Skip** | Only files that are not there yet are copied. A file with the same name and extension in the same sub-folder is left as it is. |
| **Replace** | Files that are already there are replaced. Over a network share, files on this PC are replaced only after you confirm. |

**Upload folder…** copies the folder with all its files and sub-folders, so with **Skip** you can upload the same
folder again and only the new files are copied. Harbor remembers the choice.

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
| Skipped | The file was already there and **Skip** was chosen; it was left as it is. |

**Pause** and **Resume** work on one transfer, **Resume all** on every paused one. **Cancel** stops a transfer and
deletes the partly copied file; on a finished transfer the same button **Remove**s it from the list (the file
stays). **Clear done** removes all finished rows. The window can be closed while transfers run.

## How resuming works over a network share

- Each file is copied in 4 MB chunks into `<name>.harbor-part` next to its destination.
- After every chunk is written and flushed, Harbor saves its position and a hash in a small journal
  (`transfers\<id>.json` in the [data folder](../../reference/data-and-files/)). So Harbor always knows exactly how
  much reached the destination — after a dropped connection, a closed app or a crash.
- Before continuing it checks the partial file against the journal (its length and the last chunk's hash) and copies
  again anything that did not really arrive. If the source file changed in the meantime the copy starts over.
- When all bytes are in, the partial file is renamed to its real name.

Closing Harbor while transfers are unfinished asks first; they come back **paused** at the next start and continue
from where they stopped when you press **Resume**. The journal holds no password.
