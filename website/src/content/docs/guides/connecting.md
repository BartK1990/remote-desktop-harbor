---
title: Connecting
description: What happens when you launch an entry, and how credentials reach Remote Desktop safely.
---

Activate an entry with **Connect**, a double-click, or <kbd>Enter</kbd> in the list. The standard Windows Remote
Desktop client (`mstsc.exe`) opens as **its own normal window** — it is not embedded in Harbor, so you can move,
full-screen or close it like any other Remote Desktop session.

## What happens under the hood

1. **A temporary `.rdp` file is built** in the data folder's `tmp` directory from:
   - the app's defaults (below),
   - the entry's own RDP settings (RDP connections only),
   - the app-managed keys `full address` and `username`.

   Password keys are always stripped — the file never contains a secret.
2. **The password goes to Windows Credential Manager** as a domain credential named `TERMSRV/<host>`, which is where
   `mstsc.exe` looks for saved credentials. It is never passed on a command line (where other processes could read
   it) and never written to the `.rdp` file.
3. **`%SystemRoot%\System32\mstsc.exe <file>.rdp` is started.**
4. **After 30 seconds** the temporary file is deleted and — unless you chose to keep it — the credential is removed
   again. The credential is stored with *session* persistence, so even if Harbor crashes it never survives logoff.
   Leftover `.rdp` files from a crash are swept at the next start.

### Default RDP settings

| Setting | Value | Meaning |
|---|---|---|
| `screen mode id` | `2` | Full screen |
| `use multimon` | `0` | Single monitor |
| `prompt for credentials` | `0` | Use the saved credential |
| `authentication level` | `2` | Warn if server authentication fails |
| `redirectclipboard` | `1` | Clipboard redirection on |
| `autoreconnection enabled` | `1` | Reconnect automatically after network drops |

An RDP connection entry can override any of these in its **RDP settings**.

## Keeping the credential

**Settings → Remote Desktop → Keep the password in Windows Credential Manager after connecting**

| | Off (default) | On |
|---|---|---|
| Credential lifetime | Removed ~30 s after launch; never survives logoff | Kept (local-machine persistence) |
| Reconnect from the mstsc window later | May prompt for the password | Works without Harbor |
| Exposure | Minimal | Password stays in your Credential Manager |

## Connecting to an entry that is in use

With a shared database, an entry another team member is connected to shows an **In use by …** badge. Connecting to
it asks for confirmation first, because on single-session hosts your connection may disconnect the other user. See
[Session presence](../../team/session-presence/).

## When Remote Desktop still asks for a password

Some environments block saved credentials for Remote Desktop (Credential Guard, or the *Allow delegating saved
credentials* group policy). Remote Desktop then prompts as usual — use **Copy password**
(<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>C</kbd>) and paste.

More in [Troubleshooting](../../reference/troubleshooting/).
