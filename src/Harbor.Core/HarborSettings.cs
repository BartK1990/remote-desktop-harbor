namespace Harbor.Core;

public enum StorageMode
{
    /// <summary>Single-user SQLite file on this machine.</summary>
    Local = 0,

    /// <summary>Shared SQL database (e.g. SQL Server) with a local SQLite offline cache.</summary>
    Remote = 1,
}

public enum AuthMode
{
    Windows = 0,
    EntraId = 1,
}

/// <summary>User-editable app configuration, persisted as JSON in the data directory. See SPEC section 7.</summary>
public sealed class HarborSettings
{
    public StorageMode StorageMode { get; set; } = StorageMode.Local;

    /// <summary>Registered provider name for the remote store, e.g. <c>SqlServer</c>.</summary>
    public string RemoteProvider { get; set; } = "SqlServer";

    /// <summary>Remote connection string, DPAPI-protected (base64) at rest.</summary>
    public string? RemoteConnectionStringProtected { get; set; }

    /// <summary>How a new shared vault key is protected: <c>dpapi-ng</c> or <c>passphrase</c>.</summary>
    public string RemoteKeyScheme { get; set; } = "dpapi-ng";

    /// <summary>DPAPI-NG protection descriptor, e.g. <c>SID=S-1-5-21-...</c> of an AD group.</summary>
    public string? RemoteKeyDescriptor { get; set; }

    /// <summary>Apply schema migrations to the remote database at startup (needs DDL rights).</summary>
    public bool RemoteAutoMigrate { get; set; } = true;

    public int RemoteSyncIntervalSeconds { get; set; } = 60;

    public AuthMode AuthMode { get; set; } = AuthMode.Windows;
    public string? EntraClientId { get; set; }
    public string? EntraTenantId { get; set; } = "organizations";

    public int PresenceTtlSeconds { get; set; } = 120;
    public int PresenceHeartbeatSeconds { get; set; } = 45;

    /// <summary>Keep the TERMSRV credential in Windows Credential Manager after launch (otherwise removed shortly after).</summary>
    public bool KeepCredentialAfterLaunch { get; set; }

    /// <summary>Canonical paths of the folders collapsed in the entry tree (SPEC FR-02); restored at start.</summary>
    public List<string> CollapsedFolders { get; set; } = [];
}
