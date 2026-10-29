using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Harbor.Data.Providers;

/// <summary>
/// Microsoft SQL Server / Azure SQL. Authentication is chosen by the connection string, e.g.
/// <c>Integrated Security=true</c> (Windows/domain) or <c>Authentication=Active Directory Default</c> (Entra ID).
/// </summary>
public sealed class SqlServerProvider : IDatabaseProvider
{
    public const string ProviderName = "SqlServer";

    public string Name => ProviderName;

    public DbConnection CreateConnection(string connectionString) => new SqlConnection(connectionString);

    public string NowUnixMs => "DATEDIFF_BIG(millisecond, '19700101', SYSUTCDATETIME())";

    public string? PrepareSql => null;

    public string EnsureVersionTableSql =>
        "IF OBJECT_ID(N'dbo.SchemaVersion', N'U') IS NULL CREATE TABLE dbo.SchemaVersion (Version INT NOT NULL);";

    public IReadOnlyList<string> Migrations { get; } =
    [
        """
        CREATE TABLE dbo.Connections (
            Id             NVARCHAR(36)   NOT NULL CONSTRAINT PK_Connections PRIMARY KEY,
            Name           NVARCHAR(256)  NOT NULL,
            Folder         NVARCHAR(256)  NULL,
            Kind           INT            NOT NULL,
            Host           NVARCHAR(256)  NULL,
            Port           INT            NOT NULL,
            Domain         NVARCHAR(256)  NULL,
            UserName       NVARCHAR(256)  NULL,
            RdpSettings    NVARCHAR(MAX)  NULL,
            Notes          NVARCHAR(MAX)  NULL,
            PasswordCipher VARBINARY(MAX) NULL,
            Version        BIGINT         NOT NULL,
            CreatedUtcMs   BIGINT         NOT NULL,
            UpdatedUtcMs   BIGINT         NOT NULL,
            UpdatedBy      NVARCHAR(256)  NULL,
            IsDeleted      INT            NOT NULL CONSTRAINT DF_Connections_IsDeleted DEFAULT 0
        );
        CREATE INDEX IX_Connections_UpdatedUtcMs ON dbo.Connections (UpdatedUtcMs);

        CREATE TABLE dbo.ActiveSessions (
            Id                 NVARCHAR(36)  NOT NULL CONSTRAINT PK_ActiveSessions PRIMARY KEY,
            ConnectionId       NVARCHAR(36)  NOT NULL,
            UserName           NVARCHAR(256) NOT NULL,
            DisplayName        NVARCHAR(256) NULL,
            Email              NVARCHAR(256) NULL,
            MachineName        NVARCHAR(256) NOT NULL,
            StartedUtcMs       BIGINT        NOT NULL,
            LastHeartbeatUtcMs BIGINT        NOT NULL,
            ExpiresUtcMs       BIGINT        NOT NULL
        );
        CREATE INDEX IX_ActiveSessions_ExpiresUtcMs ON dbo.ActiveSessions (ExpiresUtcMs);

        CREATE TABLE dbo.VaultKeys (
            Slot         NVARCHAR(64)   NOT NULL CONSTRAINT PK_VaultKeys PRIMARY KEY,
            KeyId        NVARCHAR(36)   NOT NULL,
            Scheme       NVARCHAR(64)   NOT NULL,
            ProtectedKey VARBINARY(MAX) NOT NULL,
            CreatedUtcMs BIGINT         NOT NULL
        );

        CREATE TABLE dbo.Meta (
            [Key]   NVARCHAR(128)  NOT NULL CONSTRAINT PK_Meta PRIMARY KEY,
            [Value] NVARCHAR(MAX)  NOT NULL
        );
        """,
    ];

    // 2627 = PK/unique constraint, 2601 = unique index
    public bool IsUniqueViolation(DbException exception) => exception is SqlException { Number: 2627 or 2601 };
}
