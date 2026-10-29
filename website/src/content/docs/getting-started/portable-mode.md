---
title: Portable mode
description: Keep the app's data in a folder next to the exe instead of your user profile.
---

By default the app stores its data in `%LOCALAPPDATA%\RemoteDesktopHarbor`. In **portable mode** it stores it in a
`data` folder next to the exe instead.

## Enable portable mode

1. Create an empty file named `portable.txt` in the same folder as `RemoteDesktopHarbor.exe`.
2. Start the app. Data now goes to `.\data`.

```
C:\Tools\RemoteDesktopHarbor\
├── RemoteDesktopHarbor.exe
├── portable.txt          ← marker file (content is ignored)
└── data\                 ← settings, vault, cache, logs
```

**Settings → Data folder** shows the active location and adds “(portable mode)” when it is on.

## Passwords stay bound to you

Portable mode moves *files*, not *trust*. Stored passwords are still encrypted with a key protected by DPAPI for
**your Windows user on this computer**. If you copy the folder to another PC, or open it as another user, the app
starts but cannot decrypt the passwords. This is intentional — see
[Security model](../../reference/security-model/).

:::caution
To move entries to another computer, use a [shared database](../../team/shared-database/) rather than copying the
`data` folder.
:::

## Limitations

- Portable mode is not available in a future Microsoft Store (MSIX) build, because the install folder is read-only
  there.
- The folder must be writable by your user.
