using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Harbor.Data.Providers;

/// <summary>
/// Microsoft SQL Server / Azure SQL. Authentication is chosen by the connection string, e.g.
/// <c>Integrated Security=true</c> (Windows/domain) or <c>Authentication=Active Directory Default</c> (Entra ID).
/// </summary>
public sealed class SqlServerProvider : IServerDatabaseProvider
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

    // SPEC FR-27, ADR-0014
    public string BuildConnectionString(DatabaseServerConnection connection, string? existing = null)
    {
        // Only touch what differs, so an unchanged form gives back an equivalent string (see CanEditAsFields).
        var builder = new SqlConnectionStringBuilder(existing ?? "");
        Set(builder.DataSource, connection.Server.Trim(), v => builder.DataSource = v);
        Set(builder.InitialCatalog, connection.Database.Trim(), v => builder.InitialCatalog = v);
        Set(builder.Encrypt != SqlConnectionEncryptOption.Optional, connection.Encrypt,
            v => builder.Encrypt = v ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional);
        Set(builder.TrustServerCertificate, connection.TrustServerCertificate, v => builder.TrustServerCertificate = v);

        var authentication = connection.Authentication switch
        {
            DatabaseAuthentication.EntraId when ToAuthentication(builder) == DatabaseAuthentication.EntraId =>
                builder.Authentication, // keep a more specific Entra method from an edited string, e.g. Managed Identity
            DatabaseAuthentication.EntraId => SqlAuthenticationMethod.ActiveDirectoryDefault,
            DatabaseAuthentication.SqlLogin when builder.Authentication == SqlAuthenticationMethod.SqlPassword => builder.Authentication,
            DatabaseAuthentication.EntraIdInteractive => SqlAuthenticationMethod.ActiveDirectoryInteractive,
            _ => SqlAuthenticationMethod.NotSpecified,
        };
        Set(builder.Authentication, authentication, v => builder.Authentication = v);
        Set(builder.IntegratedSecurity, connection.Authentication == DatabaseAuthentication.Windows, v => builder.IntegratedSecurity = v);
        if (connection.Authentication == DatabaseAuthentication.SqlLogin)
        {
            Set(builder.UserID, connection.UserName?.Trim() ?? "", v => builder.UserID = v);
            Set(builder.Password, connection.Password ?? "", v => builder.Password = v);
        }
        else
        {
            builder.Remove("User ID");
            builder.Remove("Password");
        }

        return builder.ConnectionString;

        static void Set<T>(T current, T value, Action<T> set)
        {
            if (!EqualityComparer<T>.Default.Equals(current, value))
            {
                set(value);
            }
        }
    }

    public bool CanEditAsFields(string connectionString)
    {
        try
        {
            var original = new SqlConnectionStringBuilder(connectionString);
            var rebuilt = new SqlConnectionStringBuilder(BuildConnectionString(ParseConnectionString(connectionString), connectionString));
            return original.EquivalentTo(rebuilt);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public DatabaseServerConnection ParseConnectionString(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var authentication = ToAuthentication(builder);
        var isLogin = authentication == DatabaseAuthentication.SqlLogin;
        return new DatabaseServerConnection
        {
            Server = builder.DataSource,
            Database = builder.InitialCatalog,
            Authentication = authentication,
            UserName = isLogin ? builder.UserID : null,
            Password = isLogin ? builder.Password : null,
            Encrypt = builder.Encrypt != SqlConnectionEncryptOption.Optional,
            TrustServerCertificate = builder.TrustServerCertificate,
        };
    }

    public async Task<bool> EnsureDatabaseExistsAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        var database = builder.InitialCatalog;
        if (string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException("The connection string names no database.");
        }

        // The target database may not exist yet, so ask the server's master database.
        builder.InitialCatalog = "master";
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF DB_ID(@name) IS NOT NULL
                SELECT 0;
            ELSE
            BEGIN
                DECLARE @sql NVARCHAR(MAX) = N'CREATE DATABASE ' + QUOTENAME(@name);
                EXEC (@sql);
                SELECT 1;
            END
            """;
        command.Parameters.Add(new SqlParameter("@name", System.Data.SqlDbType.NVarChar, 128) { Value = database });
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 1;
    }

    private static DatabaseAuthentication ToAuthentication(SqlConnectionStringBuilder builder) => builder.Authentication switch
    {
        SqlAuthenticationMethod.ActiveDirectoryInteractive => DatabaseAuthentication.EntraIdInteractive,
        SqlAuthenticationMethod.SqlPassword => DatabaseAuthentication.SqlLogin,
        SqlAuthenticationMethod.NotSpecified when builder.IntegratedSecurity => DatabaseAuthentication.Windows,
        SqlAuthenticationMethod.NotSpecified when !string.IsNullOrEmpty(builder.UserID) => DatabaseAuthentication.SqlLogin,
        SqlAuthenticationMethod.NotSpecified => DatabaseAuthentication.Windows,
        _ => DatabaseAuthentication.EntraId,
    };
}
