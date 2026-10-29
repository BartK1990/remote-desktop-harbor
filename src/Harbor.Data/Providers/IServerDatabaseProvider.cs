namespace Harbor.Data.Providers;

/// <summary>How the app authenticates to a shared database server. SPEC FR-43.</summary>
public enum DatabaseAuthentication
{
    /// <summary>The current Windows account (integrated security).</summary>
    Windows = 0,

    /// <summary>A database login with user name and password.</summary>
    SqlLogin = 1,

    /// <summary>Microsoft Entra ID with the signed-in account, no prompt (<c>Active Directory Default</c>).</summary>
    EntraId = 2,

    /// <summary>Microsoft Entra ID with an interactive sign-in prompt.</summary>
    EntraIdInteractive = 3,
}

/// <summary>The fields a user enters instead of a raw connection string. SPEC FR-27, ADR-0014.</summary>
public sealed record DatabaseServerConnection
{
    /// <summary>Host name or IP address, optionally with <c>,port</c> or <c>\instance</c>.</summary>
    public string Server { get; init; } = "";

    public string Database { get; init; } = "";

    public DatabaseAuthentication Authentication { get; init; }

    /// <summary>Login name; only used with <see cref="DatabaseAuthentication.SqlLogin"/>.</summary>
    public string? UserName { get; init; }

    /// <summary>Login password; only used with <see cref="DatabaseAuthentication.SqlLogin"/>. Never log it.</summary>
    public string? Password { get; init; }

    public bool Encrypt { get; init; } = true;

    public bool TrustServerCertificate { get; init; }
}

/// <summary>
/// A provider for a database <em>server</em> (as opposed to a file). It turns the simple settings form into a connection
/// string and back, and can create the database. Engine-specific, so it lives with the provider. See ADR-0014.
/// </summary>
public interface IServerDatabaseProvider : IDatabaseProvider
{
    /// <summary>
    /// Builds a connection string from <paramref name="connection"/>. Keywords of <paramref name="existing"/> that the
    /// form does not cover (timeouts, application name, ...) are kept.
    /// </summary>
    string BuildConnectionString(DatabaseServerConnection connection, string? existing = null);

    /// <summary>Reads the form fields back from a connection string.</summary>
    DatabaseServerConnection ParseConnectionString(string connectionString);

    /// <summary>
    /// <c>true</c> if the form fields describe <paramref name="connectionString"/> completely, so editing it through the
    /// form loses nothing (e.g. not for an Entra method the form does not offer).
    /// </summary>
    bool CanEditAsFields(string connectionString);

    /// <summary>Creates the database named in the connection string if it does not exist yet.</summary>
    /// <returns><c>true</c> if the database was created, <c>false</c> if it already existed.</returns>
    Task<bool> EnsureDatabaseExistsAsync(string connectionString, CancellationToken cancellationToken = default);
}
