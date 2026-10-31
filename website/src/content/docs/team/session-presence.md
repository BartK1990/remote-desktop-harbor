---
title: Session presence
description: See who on your team is connected to which machine.
---

With a shared database, Harbor shows which entries are currently in use and by whom:

> **In use by Jane Doe &lt;jane@contoso.com&gt; on PC-42 since 10:31**

In the list an entry in use shows a dot and the name of who is connected (or “2 users”) in place of its host; hover
it for the full text. The details pane lists every active session for the selected entry, with the account each one
is connected as. For an entry with
[several accounts](../../guides/managing-entries/#several-accounts-per-entry), each account row shows its own marker.

![Main window showing in-use dots for Jane Doe and Sam Patel in the list, and an "Active sessions" box in the details of the selected entry.](../../../assets/screenshots/main.png)

## How it works

Presence uses **leases** with an expiry, so crashed or disconnected clients never leave “ghost” sessions behind.

1. When you connect, the app writes a record to `ActiveSessions`: entry, the account you connect as, your user
   name, display name, e-mail, machine name and start time, **expiring after the TTL** (default 120 s).
2. While the Remote Desktop window is open, the app refreshes the record every **heartbeat** (default 45 s).
3. When the Remote Desktop window closes, the record is deleted.
4. Everyone's app lists only records that have not expired, refreshing the view every 20 s.

All times come from the **database clock**, so a client with a wrong clock cannot make sessions look alive or dead.

| Event | Effect |
|---|---|
| You close the Remote Desktop window (or disconnect) | Record deleted immediately |
| You close Harbor with sessions still open | Harbor asks for confirmation, then ends its records |
| Harbor crashes, laptop sleeps, network drops | Record disappears from everyone's view after at most one TTL |
| Database unreachable when you connect | The session starts anyway; presence is published once the database is back |

Expired records older than a day are purged automatically.

## Connecting to an entry in use

Connecting with an account that someone is already using asks for confirmation — your connection would take over
their session. Connecting with a **different account** of the same entry does not ask: hosts such as Windows Server
run several sessions side by side. Sessions published by older app versions do not record the account, so they
always ask; so does connecting without a user name.

![Confirmation dialog "Connection in use" with Yes and No buttons.](../../../assets/screenshots/inuse.png)

## Settings

**Settings → Shared vault → Presence timeout / Presence heartbeat (s)**

| Setting | Default | Rule |
|---|---|---|
| Timeout (TTL) | 120 | How long a record lives without a heartbeat |
| Heartbeat | 45 | Must be at most half the timeout |

Lower values make crashed sessions disappear faster at the cost of more database writes.

## Limitations

- Only sessions **started from Harbor** are visible — not ones opened directly with `mstsc.exe`.
- Presence needs the shared database; there is no presence in a local vault.
- On hosts that allow only one session (e.g. Windows 10/11), connecting with another account still disconnects the
  other person, without asking.
- While your client is offline it shows no presence at all, rather than stale data.
