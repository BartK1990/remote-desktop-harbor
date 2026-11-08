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

    /// <summary>No sign-in; the user enters a name (<see cref="HarborSettings.AnonymousName"/>). SPEC FR-44</summary>
    Anonymous = 2,
}

/// <summary>Colour theme of the app. SPEC FR-08</summary>
public enum AppTheme
{
    /// <summary>Follow the Windows light/dark setting.</summary>
    System = 0,
    Light = 1,
    Dark = 2,
}

/// <summary>Helpers for <see cref="AppTheme"/>. SPEC FR-08</summary>
public static class AppThemeExtensions
{
    /// <summary>Whether <paramref name="theme"/> shows dark colours, given the current Windows app mode.</summary>
    public static bool IsDark(this AppTheme theme, bool windowsUsesDarkMode) => theme switch
    {
        AppTheme.Light => false,
        AppTheme.Dark => true,
        _ => windowsUsesDarkMode,
    };
}

/// <summary>How file transfers reach the host (SPEC FR-60, FR-63).</summary>
public enum TransferMethod
{
    /// <summary>Through the Remote Desktop connection: only the Remote Desktop port is needed (ADR-0041).</summary>
    RemoteDesktop = 0,

    /// <summary>Over Windows file sharing (SMB, port 445) with the entry's account (ADR-0039).</summary>
    NetworkShare = 1,
}

/// <summary>User-editable app configuration, persisted as JSON in the data directory. See SPEC section 7.</summary>
public sealed class HarborSettings
{
    public StorageMode StorageMode { get; set; } = StorageMode.Local;

    /// <summary>Registered provider name for the remote store, e.g. <c>SqlServer</c>.</summary>
    public string RemoteProvider { get; set; } = "SqlServer";

    /// <summary>Remote connection string, DPAPI-protected (base64) at rest.</summary>
    public string? RemoteConnectionStringProtected { get; set; }

    /// <summary>
    /// How a new shared vault key is protected: <c>sign-in</c> (not protected: key stored unencrypted in the database),
    /// <c>dpapi-ng</c> or <c>passphrase</c>. See ADR-0016.
    /// </summary>
    public string RemoteKeyScheme { get; set; } = "sign-in";

    /// <summary>
    /// One-time request: on the next connection, re-protect an existing shared vault key with
    /// <see cref="RemoteKeyScheme"/>. Cleared once done. SPEC FR-28
    /// </summary>
    public bool RemoteKeyConvert { get; set; }

    /// <summary>DPAPI-NG protection descriptor, e.g. <c>SID=S-1-5-21-...</c> of an AD group.</summary>
    public string? RemoteKeyDescriptor { get; set; }

    /// <summary>Apply schema migrations to the remote database at startup (needs DDL rights).</summary>
    public bool RemoteAutoMigrate { get; set; } = true;

    public int RemoteSyncIntervalSeconds { get; set; } = 60;

    public AuthMode AuthMode { get; set; } = AuthMode.Windows;
    public string? EntraClientId { get; set; }
    public string? EntraTenantId { get; set; } = "organizations";

    /// <summary>Name shown for presence and audit when <see cref="AuthMode"/> is <see cref="AuthMode.Anonymous"/>.</summary>
    public string? AnonymousName { get; set; }

    public int PresenceTtlSeconds { get; set; } = 120;
    public int PresenceHeartbeatSeconds { get; set; } = 45;

    /// <summary>Keep the TERMSRV credential in Windows Credential Manager after launch (otherwise removed shortly after).</summary>
    public bool KeepCredentialAfterLaunch { get; set; }

    /// <summary>Ask before connecting to an entry tagged PROD (SPEC FR-51, FR-49).</summary>
    public bool ConfirmProductionConnect { get; set; } = true;

    /// <summary>Colour theme, chosen in the main toolbar and applied immediately (SPEC FR-08).</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Canonical paths of the folders collapsed in the entry tree (SPEC FR-02); restored at start.</summary>
    public List<string> CollapsedFolders { get; set; } = [];

    /// <summary>Zoom level of the entry tree rows, 0.8 to 1.6 (SPEC FR-55); see <see cref="Model.TreeZoom"/>.</summary>
    public double TreeZoom { get; set; } = Model.TreeZoom.Default;

    /// <summary>
    /// Width of the entry list pane as a share of the list and details panes, 0.1 to 0.9 (SPEC FR-56);
    /// see <see cref="Model.ListPaneWidth"/>.
    /// </summary>
    public double ListPaneWidth { get; set; } = Model.ListPaneWidth.Default;

    /// <summary>
    /// Folder on the entry's host that file transfers start in (SPEC FR-60): relative to <c>\\host\</c>, or a full UNC path.
    /// Remembered from the last transfer.
    /// </summary>
    public string TransferRemoteFolder { get; set; } = @"C$\Users\Public\Downloads";

    /// <summary>How file transfers reach the host; the last choice in the File transfer window (SPEC FR-63).</summary>
    public TransferMethod TransferMethod { get; set; } = TransferMethod.RemoteDesktop;

    /// <summary>
    /// Remote Desktop transfers: folder on the server, as the server sees it; environment variables are expanded there
    /// (SPEC FR-63). Remembered from the last transfer.
    /// </summary>
    public string TransferServerFolder { get; set; } = @"%USERPROFILE%\Downloads";

    /// <summary>What file transfers do with files that already exist at the destination; the last choice (SPEC FR-64).</summary>
    public Transfer.ExistingFiles TransferExistingFiles { get; set; } = Transfer.ExistingFiles.Fail;

    /// <summary>
    /// Smart groups of this user (SPEC FR-62, ADR-0040): virtual tree groups listing the entries their rules match.
    /// Personal, like pins: never written to a shared database.
    /// </summary>
    public List<Model.SmartGroup> SmartGroups { get; set; } = [];

    /// <summary>Smart groups collapsed in the entry tree (SPEC FR-62); restored at start.</summary>
    public List<Guid> CollapsedSmartGroups { get; set; } = [];

    /// <summary>Entries this user pinned (SPEC FR-50); personal, never written to a shared database.</summary>
    public List<Guid> PinnedEntryIds { get; set; } = [];

    /// <summary>Entries this user connected to, most recent first, at most <see cref="Model.RecentList.Capacity"/> (SPEC FR-50).</summary>
    public List<Guid> RecentEntryIds { get; set; } = [];
}
