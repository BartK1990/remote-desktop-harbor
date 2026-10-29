using System.Data.Common;

namespace Harbor.Data.Providers;

/// <summary>
/// Everything that differs between SQL engines. Adding an engine (PostgreSQL, MySQL, ...) means implementing
/// this interface and registering it in <see cref="DatabaseProviders"/>; the store code is shared. See ADR-0004.
/// </summary>
public interface IDatabaseProvider
{
    /// <summary>Name used in settings, e.g. <c>Sqlite</c>, <c>SqlServer</c>.</summary>
    string Name { get; }

    DbConnection CreateConnection(string connectionString);

    /// <summary>SQL expression for the database clock as Unix milliseconds (UTC).</summary>
    string NowUnixMs { get; }

    /// <summary>Statements run on a fresh connection before migrating, outside any transaction (may be empty).</summary>
    string? PrepareSql { get; }

    /// <summary>Creates the schema version table if missing.</summary>
    string EnsureVersionTableSql { get; }

    /// <summary>Ordered schema migrations; index 0 brings an empty database to version 1.</summary>
    IReadOnlyList<string> Migrations { get; }

    bool IsUniqueViolation(DbException exception);
}
