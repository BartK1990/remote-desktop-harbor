---
title: Data model
description: Database tables used by the local vault, the cache and the shared database.
---

The same schema is used in every store — local vault, local cache and shared database. All timestamps are
**Unix milliseconds (UTC) from the database clock**; ids are GUID strings.

## Tables

### `Connections`

| Column | Type | Notes |
|---|---|---|
| `Id` | GUID text | Primary key |
| `Name` | text | Required |
| `Folder` | text, nullable | Grouping |
| `Kind` | int | `0` RDP connection, `1` credentials only |
| `Host`, `Port` | text, int | Port defaults to 3389 |
| `Domain`, `UserName` | text, nullable | |
| `RdpSettings` | text, nullable | Extra `.rdp` lines |
| `Notes` | text, nullable | |
| `PasswordCipher` | binary, nullable | AES-GCM envelope — see [Security model](../security-model/) |
| `Version` | bigint | Optimistic concurrency token, incremented on every write |
| `CreatedUtcMs`, `UpdatedUtcMs` | bigint | Database clock |
| `UpdatedBy` | text, nullable | Signed-in user who last changed the row |
| `IsDeleted` | int | Tombstone, so deletions reach every cache |

### `ActiveSessions`

[Presence](../../team/session-presence/) records. Only used in the shared database.

| Column | Notes |
|---|---|
| `Id` | Primary key |
| `ConnectionId` | Entry in use |
| `UserName`, `DisplayName`, `Email` | Who |
| `MachineName` | From where |
| `StartedUtcMs`, `LastHeartbeatUtcMs`, `ExpiresUtcMs` | Lease; readers only show rows with `ExpiresUtcMs > now` |

### `VaultKeys`

| Column | Notes |
|---|---|
| `Slot` | `primary` |
| `KeyId` | Matches the key id in each password envelope |
| `Scheme` | Key protector, e.g. `dpapi-ng`, `passphrase`, `dpapi-user+machine` |
| `ProtectedKey` | The wrapped DEK |
| `CreatedUtcMs` | |

### `Meta` and `SchemaVersion`

`Meta` is a key/value table (for example the sync watermark in a cache). `SchemaVersion` records applied migrations.

## Sync

A cache pulls rows with `UpdatedUtcMs` greater than its watermark minus five minutes; the overlap absorbs
commit-order skew, and applying a row is idempotent.
