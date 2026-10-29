# ADR-0004: Data access — ADO.NET + Dapper with pluggable SQL providers

* Status: Accepted
* Date: 2026-10-29
* Requirements: FR-20, FR-21, FR-25, NFR-01, NFR-20

## Context

The same data must live in a local SQLite file, a local cache, and a shared SQL Server database, and other SQL
engines should be addable later. Startup time matters.

## Options considered

1. **EF Core** — rich, migrations per provider. Model building adds startup cost (mitigated by compiled models),
   migrations must be generated per provider, larger dependency surface.
2. **ADO.NET + Dapper + hand-written portable SQL** — tiny and fast, full control over SQL, a provider is ~100 lines.
3. Raw ADO.NET — fastest, but lots of mapping boilerplate.

## Decision

Option 2.

* One store implementation `SqlHarborStore` (implements `IConnectionStore` and `IPresenceStore`) uses only portable SQL.
* Everything engine-specific is in `IDatabaseProvider`: connection factory, "now as Unix ms" expression,
  version-table DDL, ordered migration scripts, unique-violation detection. Providers: `Sqlite`, `SqlServer`;
  `DatabaseProviders.Register()` adds more (PostgreSQL, MySQL, …).
* Portability rules: ids as GUID text, timestamps as BIGINT Unix ms from the **database clock**, booleans as INT,
  upserts as UPDATE-then-INSERT inside a transaction (no MERGE / ON CONFLICT).
* Schema versioning through a `SchemaVersion` table; migrations run in transactions; automatic migration of the shared
  DB is optional (needs DDL rights).

## Consequences

* New engine = new provider class + its DDL; no business-logic change.
* SQL is reviewed by hand; tests run against real SQLite on every CI build. SQL Server is exercised manually / by an
  integration environment (future: containerized SQL Server job in CI).
