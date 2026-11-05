---
title: Smart groups
description: Virtual groups in the entry tree that list every entry matching your rules, like smart groups in Remote Desktop Connection Manager.
---

A **smart group** is a group in the entry tree that holds no entries of its own: it lists every entry that matches
its rules, and keeps up as entries are added, edited, synced or deleted. If you used **Remote Desktop Connection
Manager** (RDCMan), it works like its smart groups — for example *every SQL server*, *everything in production
that runs as `svc-admin`*, or *hosts matching `^web-\d+`* — without moving any entry out of its folder.

## Create a smart group

1. Right-click in the entry tree and choose **New smart group…**. If a folder is selected, the group is created in
   that folder.
2. Give it a **Name**.
3. **Show in folder** (optional): the group is shown in that folder, before its sub-folders, and only looks at
   entries in that folder and below. Leave it empty to show the group at the top of the tree and look at every entry.
4. Choose whether entries must match **all rules** or **any rule**.
5. Add rules with **Add rule**; remove one with its **×** button. The bottom of the editor shows how many entries
   match while you type.
6. **Save** (<kbd>Ctrl</kbd>+<kbd>S</kbd> or <kbd>Enter</kbd>).

A group without rules lists every entry of its folder.

## Rules

Each rule is *field — comparison — value*. Text is compared without regard to case.

| Field | Compares |
|---|---|
| **Name** | The entry's name |
| **Host** | The host name or IP address (without the port) |
| **Folder** | The folder path, e.g. `Production/Web` (empty for no folder) |
| **User name** | Every account of the entry as `DOMAIN\user`; the rule holds if one account matches. *does not contain* / *is not* hold only if no account matches |
| **Notes** | The notes |
| **Environment** | The [environment tag](../managing-entries/#the-folder-tree) from the folder: `PROD`, `STG`, `TEST`, `DEV` or `LAB` (empty for none) |
| **Kind** | `RDP`, `Credentials` or `Other` |

| Comparison | Holds when the field… |
|---|---|
| **contains** / **does not contain** | has / does not have the value somewhere |
| **is** / **is not** | equals / differs from the value. An empty value means "the field is empty" |
| **starts with** / **ends with** | begins / ends with the value |
| **matches regex** / **does not match regex** | matches a .NET regular expression somewhere, e.g. `^sql-\d+\.` |

An invalid regular expression is reported when you save and never matches.

### Examples

| Smart group | Match | Rules |
|---|---|---|
| SQL servers in production | all rules | **Host** *starts with* `sql-` · **Environment** *is* `PROD` |
| Admin sessions | any rule | **User name** *contains* `admin` · **User name** *ends with* `-adm` |
| Not reachable with Remote Desktop | all rules | **Kind** *is* `Other` |
| Entries without a folder | all rules | **Folder** *is* (empty) |

## In the tree

- A smart group shows a filter icon, its name in italics and the number of entries it lists. Hover over it to read
  its rules.
- Its rows are the same entries as in their folders: selecting one shows the entry in the details pane, and
  **Connect**, copy, pin, edit and delete act on that entry. The entry keeps its own row in its folder.
- The search box and the **All / In use / Pinned / Recent** filters apply inside smart groups too. While searching,
  smart groups without matches are hidden.
- Expand and collapse smart groups like folders; collapsed groups are remembered. **Expand all** and **Collapse all**
  include them.
- Edit or delete a group from the context menu (**Edit smart group…**, **Delete smart group**), or select it and
  press <kbd>F2</kbd> or <kbd>Del</kbd>. Deleting a smart group never deletes its entries.

## Personal, not shared

Smart groups are saved in your own `settings.json` (`SmartGroups`, see [Settings](../../reference/settings/)), like
pins. They are **not** written to the shared database, so in a team each person keeps their own groups, and they
work the same offline and with [entries on this PC only](../../team/local-entries/). They do not follow you to
another computer.
