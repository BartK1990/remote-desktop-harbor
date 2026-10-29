---
title: Database providers
description: How SQL engines are plugged in, and how to add a new one.
---

All storage goes through one store, `SqlHarborStore`, which uses **portable SQL only**. Everything that differs
between engines lives in an `IDatabaseProvider`. Built-in providers:

| Name | Used for |
|---|---|
| `Sqlite` | Local vault and local caches |
| `SqlServer` | Shared database (SQL Server, Azure SQL) |

## The interface

```csharp
public interface IDatabaseProvider
{
    string Name { get; }                                   // e.g. "Sqlite", "SqlServer"
    DbConnection CreateConnection(string connectionString);
    string NowUnixMs { get; }                              // SQL expression: database clock as Unix ms (UTC)
    string? PrepareSql { get; }                            // run before migrating, outside a transaction
    string EnsureVersionTableSql { get; }                  // creates SchemaVersion if missing
    IReadOnlyList<string> Migrations { get; }              // index 0 brings an empty database to version 1
    bool IsUniqueViolation(DbException exception);
}
```

## Portability rules

- Ids are GUID **text**; timestamps are BIGINT **Unix milliseconds from the database clock**; booleans are INT.
- Upserts are UPDATE-then-INSERT inside a transaction — no `MERGE` or `ON CONFLICT`.
- Migrations run in transactions, tracked in `SchemaVersion`.

## Adding an engine (e.g. PostgreSQL)

1. Implement `IDatabaseProvider` in `Harbor.Data/Providers` with the engine's ADO.NET driver (add the package version
   to `Directory.Packages.props`).
2. Write its migrations so they produce the same tables and columns as the existing providers — see
   [Data model](../../reference/data-model/).
3. Register it in `DatabaseProviders` (or call `DatabaseProviders.Register(...)`). It then appears in
   **Settings → Storage → Provider**.
4. For a server engine, also implement `IServerDatabaseProvider` (build/parse the connection string from the
   Settings fields, create the database). Without it, Settings offers only the raw connection string.
5. Add tests and update the spec.

No business logic changes are needed.

## Schema changes

A schema change means **appending** a migration to the `Migrations` list of **every** provider. Never edit a
migration that has already been applied — existing databases would not pick it up.
