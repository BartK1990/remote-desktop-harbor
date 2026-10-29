# ADR-0014: Shared database configured by form fields via the provider; optional database creation

* Status: Accepted
* Date: 2026-10-29
* Requirements: FR-26, FR-27, FR-43, NFR-12, NFR-20

## Context

Settings asked for a raw ADO.NET connection string. Users want to type a server (often an IP), a database name and a
user/password, and to have the database created if it is missing. Connection-string keywords and the SQL to create a
database differ per engine, and NFR-20 keeps engine specifics in the provider class.

The dialog also had a bug: all radio buttons of the Storage and Sign-in sections sat in one panel without a
`GroupName`, so WPF treated them as one group; choosing a sign-in option cleared the storage choice, and the dialog
could open (and save) the wrong storage mode.

## Options considered

1. Keep the raw string, add examples — no change for the user; still error-prone.
2. Build the string in the WPF view model with `SqlConnectionStringBuilder` — simple, but puts SQL Server specifics
   in the UI and does not scale to other engines.
3. A provider capability `IServerDatabaseProvider` (build/parse the string from fields, check the form can describe
   a string, create the database) — engine specifics stay in the provider; the UI is generic.

## Decision

Option 3.

* `DatabaseServerConnection` holds the fields: server, database, authentication (Windows, SQL login, Entra ID
  default, Entra ID interactive), user, password, encrypt, trust server certificate.
* `BuildConnectionString` starts from the previous string and changes only what the form changed, so extra keywords
  (timeouts, application name, …) survive. `CanEditAsFields` is false when the form cannot describe a string (e.g.
  `Active Directory Password`); the dialog then shows it as text instead of silently rewriting it.
* `EnsureDatabaseExistsAsync` (SQL Server: connect to `master`, `IF DB_ID(@name) IS NULL` + `CREATE DATABASE` via
  `QUOTENAME`, parameterized) runs only on **Test connection** and **Save** when the user ticks the option — never at
  app start (NFR-01), and it is not a persisted setting.
* A SQL login password lives only inside the DPAPI-protected connection string (SPEC §7, NFR-12); it is entered in a
  `PasswordBox` and never logged.
* Every radio-button pair has its own `GroupName`, and the inverse view-model properties react only to a check, not to
  an uncheck.

## Consequences

* Adding an engine with a server means implementing `IServerDatabaseProvider`; file-based engines (SQLite) do not.
* The raw editor remains for advanced cases.
* Creating the database needs `CREATE DATABASE` rights on the server; without them the user creates it as before.
