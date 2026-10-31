---
title: Shared database
description: Share one encrypted catalog with your team through SQL Server or Azure SQL.
---

In **shared mode** all entries — including their encrypted passwords — live in one SQL database that the whole team
uses. Every client keeps a local, still-encrypted copy for speed and [offline use](../offline-mode/), and the shared
database powers [session presence](../session-presence/).

```
 Alice's PC ──┐                                  ┌── Bob's PC
  local cache │◄──── sync (ciphertext only) ────►│ local cache
              └──────► SQL Server / Azure SQL ◄──┘
     Connections · ConnectionAccounts · VaultKeys · ActiveSessions
```

Supported engines in v1: **SQL Server** and **Azure SQL**. Other engines can be added as
[database providers](../../development/database-providers/).

## 1. Create the database

Create an **empty** database (for example `Harbor`) — or let the app create it with **Create the database if it does
not exist** in Settings (needs `CREATE DATABASE` rights) — and grant the team:

| Role | Why |
|---|---|
| `db_datareader` + `db_datawriter` | Everyday use: read, edit, presence |
| DDL rights (e.g. `db_ddladmin`) | **First start only**, to create the schema — or for schema upgrades |

If team members should not have DDL rights, have a DBA start the app once with
**Create / upgrade database schema automatically** enabled, then turn the option off for everyone else.
The schema is versioned; newer app versions add migrations, never edit old ones.

:::note
Who may connect to the database is controlled entirely by the database's own authentication — Windows
integrated, SQL Server login or Entra ID. The app's sign-in identity is used for presence and audit only.
:::

## 2. Decide how the vault key is protected

All entries are encrypted with one shared vault key. Decide who can unlock it — see
[Vault key protection](../vault-key-protection/) for details:

- **Not protected (default)** — the key is stored unencrypted in the database: everyone who can connect to the
  database (Windows, SQL login or Entra ID, with any app sign-in) opens the vault, and so can database administrators
  and anyone with a backup.
- **Active Directory group (strongest)** — create an AD security group (e.g. *RDP Vault Users*) and note its SID:

  ```powershell
  Get-ADGroup "RDP Vault Users" | Select-Object SID
  ```

- **Shared passphrase** — for environments without Active Directory.

## 3. Configure the app

Open **Settings → Storage** and choose the **Shared database** card:

| Field | Value |
|---|---|
| Provider | `SqlServer` |
| Server | Name or IP address, e.g. `10.0.0.5`, `sql01\SQLEXPRESS`, `10.0.0.5,1433`, `contoso.database.windows.net` |
| Database name | e.g. `Harbor`. Tick **Create the database if it does not exist** to have it created. |
| Database sign-in | **Windows account**, **SQL Server login** (user + password), or **Microsoft Entra ID** |
| Encrypt / Trust server certificate | Encrypt is on by default. Tick *Trust the server certificate* for servers with a self-signed certificate (typical when connecting by IP). |
| Shared vault key protection (*Shared vault* section) | **Not protected** (default, key stored unencrypted), **Active Directory group** with `SID=S-1-5-21-…`, or **Shared passphrase** |
| Create / upgrade schema automatically (*Shared vault* section) | On for the first client (needs DDL rights) |
| Sync interval (*Shared vault* section) | Default `60` seconds (minimum 10) |

Below the fields, a preview shows the connection string they produce, with the password masked. Use
**Test connection** to check the settings before saving. Everything is stored as one connection string,
encrypted for your Windows account (a SQL login password included). The database sign-in is independent of the
app's own [sign-in](../entra-id/): Windows or Entra ID app sign-in both work with a shared database.

![Settings dialog, Storage section, with the Shared database card selected: server sql01, database Harbor, SQL Server login with user name and password, and Test connection.](../../../assets/screenshots/settings.png)

### Connection string examples

Tick **Edit as connection string (advanced)** to type a raw connection string instead of the fields:

SQL Server with Windows authentication:

```text
Server=sql01;Database=Harbor;Integrated Security=true;Encrypt=true
```

Azure SQL with Entra ID (uses your signed-in Windows / Entra account):

```text
Server=tcp:contoso.database.windows.net;Database=Harbor;Authentication=Active Directory Default
```

SQL Server login on a server reached by IP:

```text
Server=10.0.0.5;Database=Harbor;User ID=harbor;Password=…;Encrypt=true;TrustServerCertificate=true
```

Interactive Entra ID sign-in:

```text
Server=tcp:contoso.database.windows.net;Database=Harbor;Authentication=Active Directory Interactive
```

## 4. Restart

Click **Save** and confirm the restart. The **first** client to connect creates the schema (if allowed) and the
vault key; everyone else picks the key up automatically. The status bar shows
**Shared database · online · synced HH:MM:SS**.

## Rolling it out to the team

Each team member sets the same provider and connection string. The key protection choice only matters for the
client that creates the vault — existing vaults keep the protection they were created with.

:::tip
Entries from a previous *local* vault are not moved automatically. Switching storage mode opens a different
database; the local vault file stays where it is, so you can switch back.
:::

## How syncing works

- The list is always shown from the local copy first, so startup never waits for the network.
- Changes are pulled incrementally at start, every sync interval, on <kbd>F5</kbd> and after each of your own
  changes. Deletions propagate to everyone.
- Your edits are written to the shared database first and then to your local copy.
- If two people edit the same entry, the second save is rejected rather than overwriting the first
  (optimistic concurrency).
- Each shared database gets its own local cache file, so switching databases never mixes data.
