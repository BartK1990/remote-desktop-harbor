---
title: Troubleshooting
description: Common problems and how to solve them.
---

Start with the log: `logs\harbor.log` in the [data folder](../data-and-files/). It never contains passwords.

## Remote Desktop asks for the password anyway

- A policy may block saved credentials for Remote Desktop (Windows Defender Credential Guard, or the
  *Allow delegating saved credentials* policy). Use **Copy password** (<kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>C</kbd>)
  and paste it into the prompt.
- Check that the entry has a **user name** — without one, no credential is handed over.
- A reconnect started from the Remote Desktop window more than ~30 s after launch may prompt, because the
  credential has already been removed. Enable **Settings → Remote Desktop → Keep the password…** if you need this.

## Connect is disabled

The entry has no host — credentials-only entries may omit it. Add a host to connect, or use **Copy user** /
**Copy password**. Connect is also disabled until the app has finished starting.

## The status bar says OFFLINE

The shared database is unreachable; the app works read-only from its local copy. Check VPN / network and the
connection string (**Settings → Test connection**). The app retries automatically; press <kbd>F5</kbd> to retry now.
See [Offline mode](../../team/offline-mode/).

## “Not saved” after editing

Someone else changed the entry in the shared database since you opened it. The app has synced their change — open
the entry again and re-apply your edit.

## The app cannot start with a shared database

- The first start needs DDL rights to create the schema, unless **Create / upgrade schema automatically** is off
  and a DBA created it.
- With AD-group protection, your account must be a member of the group, and a domain controller must be reachable
  the first time you unlock the vault on this machine.
- With a passphrase vault, you are asked for the passphrase once per machine; it must match the one chosen when
  the vault was created.

The error dialog points to **Settings**, where you can switch back to the local vault.

## Passwords cannot be decrypted after moving to a new PC

Local vaults and caches are bound to your Windows user **and** machine. Copying them elsewhere — including a
portable folder — cannot decrypt passwords. Use a shared database to move entries between machines.

## Someone still shows as “In use” after closing their session

If their app crashed or lost network, the record disappears after at most one presence timeout (default 120 s).
