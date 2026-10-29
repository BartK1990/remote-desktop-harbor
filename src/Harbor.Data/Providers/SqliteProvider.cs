using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace Harbor.Data.Providers;

public sealed class SqliteProvider : IDatabaseProvider
{
    public const string ProviderName = "Sqlite";

    public string Name => ProviderName;

    public DbConnection CreateConnection(string connectionString) => new SqliteConnection(connectionString);

    public static string ConnectionStringForFile(string path) =>
        new SqliteConnectionStringBuilder { DataSource = path, Pooling = true, Mode = SqliteOpenMode.ReadWriteCreate }.ToString();

    public string NowUnixMs => "CAST(ROUND((julianday('now') - 2440587.5) * 86400000.0) AS INTEGER)";

    public string? PrepareSql => "PRAGMA journal_mode=WAL;";

    public string EnsureVersionTableSql => "CREATE TABLE IF NOT EXISTS SchemaVersion (Version INTEGER NOT NULL);";

    public IReadOnlyList<string> Migrations { get; } =
    [
        """
        CREATE TABLE Connections (
            Id             TEXT    NOT NULL PRIMARY KEY,
            Name           TEXT    NOT NULL,
            Folder         TEXT    NULL,
            Kind           INTEGER NOT NULL,
            Host           TEXT    NULL,
            Port           INTEGER NOT NULL,
            Domain         TEXT    NULL,
            UserName       TEXT    NULL,
            RdpSettings    TEXT    NULL,
            Notes          TEXT    NULL,
            PasswordCipher BLOB    NULL,
            Version        INTEGER NOT NULL,
            CreatedUtcMs   INTEGER NOT NULL,
            UpdatedUtcMs   INTEGER NOT NULL,
            UpdatedBy      TEXT    NULL,
            IsDeleted      INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IX_Connections_UpdatedUtcMs ON Connections (UpdatedUtcMs);

        CREATE TABLE ActiveSessions (
            Id                 TEXT    NOT NULL PRIMARY KEY,
            ConnectionId       TEXT    NOT NULL,
            UserName           TEXT    NOT NULL,
            DisplayName        TEXT    NULL,
            Email              TEXT    NULL,
            MachineName        TEXT    NOT NULL,
            StartedUtcMs       INTEGER NOT NULL,
            LastHeartbeatUtcMs INTEGER NOT NULL,
            ExpiresUtcMs       INTEGER NOT NULL
        );
        CREATE INDEX IX_ActiveSessions_ExpiresUtcMs ON ActiveSessions (ExpiresUtcMs);

        CREATE TABLE VaultKeys (
            Slot         TEXT    NOT NULL PRIMARY KEY,
            KeyId        TEXT    NOT NULL,
            Scheme       TEXT    NOT NULL,
            ProtectedKey BLOB    NOT NULL,
            CreatedUtcMs INTEGER NOT NULL
        );

        CREATE TABLE Meta (
            [Key]   TEXT NOT NULL PRIMARY KEY,
            [Value] TEXT NOT NULL
        );
        """,

        // 2: additional accounts per entry, account used by a session (SPEC FR-09, ADR-0019).
        """
        CREATE TABLE ConnectionAccounts (
            Id             TEXT    NOT NULL PRIMARY KEY,
            ConnectionId   TEXT    NOT NULL,
            Position       INTEGER NOT NULL,
            Domain         TEXT    NULL,
            UserName       TEXT    NOT NULL,
            PasswordCipher BLOB    NULL
        );
        CREATE INDEX IX_ConnectionAccounts_ConnectionId ON ConnectionAccounts (ConnectionId);

        ALTER TABLE ActiveSessions ADD COLUMN AccountName TEXT NULL;
        """,
    ];

    // SQLITE_CONSTRAINT = 19
    public bool IsUniqueViolation(DbException exception) => exception is SqliteException { SqliteErrorCode: 19 };
}
