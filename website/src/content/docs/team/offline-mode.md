---
title: Offline mode
description: Keep working when the shared database is unreachable.
---

When Harbor uses a shared database, every client keeps a **local copy** of the shared data in a SQLite file — still
encrypted. If the shared database cannot be reached (VPN down, travelling, server maintenance), the app keeps working
from that copy.

## What works offline

| Action | Offline |
|---|---|
| Start the app, browse, search | ✅ |
| Connect to an entry | ✅ |
| Copy user name / password | ✅ |
| Create, import, edit, delete | ❌ disabled |
| See who is connected | ❌ (no presence shown) |

The status bar shows **Shared database · OFFLINE (read-only cached copy)** in orange, and the reason on the left.

## Getting back online

The app retries on every sync tick (default every 60 s) and switches back automatically. Press <kbd>F5</kbd> to retry
immediately. Sessions you started while offline publish their presence as soon as the database is back.

## Why edits are disabled offline

Queueing offline edits and replaying them later could create merge conflicts on secrets. To keep the shared vault
consistent, v1 makes the offline copy read-only.

## Requirements

- The client must have opened the shared vault **online at least once**, so that the local copy and the cached vault
  key exist.
- The local copy is bound to your Windows user and this machine, like a local vault.
