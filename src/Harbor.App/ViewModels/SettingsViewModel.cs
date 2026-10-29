using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Harbor.App.Infrastructure;
using Harbor.Core;
using Harbor.Core.Security;
using Harbor.Data.Providers;
using Harbor.Windows.Security;

namespace Harbor.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly HarborSettings _settings;

    public SettingsViewModel(HarborSettings settings)
    {
        _settings = settings;
        _isRemote = settings.StorageMode == StorageMode.Remote;
        _remoteProvider = settings.RemoteProvider;
        _connectionString = SafeGetConnectionString(settings);
        LoadFields(_connectionString);
        _keyScheme = settings.RemoteKeyScheme switch
        {
            PassphraseKeyProtector.SchemeName or DpapiNgKeyProtector.SchemeName => settings.RemoteKeyScheme,
            _ => SignInKeyProtector.SchemeName,
        };
        _convertKey = settings.RemoteKeyConvert;
        _keyDescriptor = settings.RemoteKeyDescriptor;
        _autoMigrate = settings.RemoteAutoMigrate;
        _syncIntervalSeconds = settings.RemoteSyncIntervalSeconds;
        _authMode = settings.AuthMode;
        _anonymousName = settings.AnonymousName;
        _entraClientId = settings.EntraClientId;
        _entraTenantId = settings.EntraTenantId;
        _presenceTtlSeconds = settings.PresenceTtlSeconds;
        _presenceHeartbeatSeconds = settings.PresenceHeartbeatSeconds;
        _keepCredential = settings.KeepCredentialAfterLaunch;
    }

    public IEnumerable<string> Providers => DatabaseProviders.Names.Where(n => n != SqliteProvider.ProviderName);

    public IReadOnlyList<KeyValuePair<DatabaseAuthentication, string>> DbAuthenticationOptions { get; } =
    [
        new(DatabaseAuthentication.Windows, "Windows account (integrated)"),
        new(DatabaseAuthentication.SqlLogin, "SQL Server login (user + password)"),
        new(DatabaseAuthentication.EntraId, "Microsoft Entra ID (signed-in account)"),
        new(DatabaseAuthentication.EntraIdInteractive, "Microsoft Entra ID (interactive prompt)"),
    ];

    public string AboutText => $"{AppInfo.ProductName} · {AppInfo.Description}";
    public string DataDirectory => AppPaths.DataDirectory + (AppPaths.IsPortable ? "  (portable mode)" : "");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocal))]
    private bool _isRemote;

    // The string the fields were loaded from, so keywords the form does not show survive editing. SPEC FR-27
    private string? _baseConnectionString;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SupportsFields), nameof(ShowFields))]
    private string _remoteProvider;

    [ObservableProperty] private string? _connectionString;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFields))]
    private bool _editRawConnectionString;

    [ObservableProperty] private string? _dbServer;
    [ObservableProperty] private string? _dbName = "Harbor";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsesSqlLogin))]
    private DatabaseAuthentication _dbAuthentication;

    [ObservableProperty] private string? _dbUserName;
    [ObservableProperty] private string? _dbPassword;
    [ObservableProperty] private bool _dbEncrypt = true;
    [ObservableProperty] private bool _dbTrustServerCertificate;
    [ObservableProperty] private bool _createDatabaseIfMissing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseSignInKey), nameof(UseDpapiNg), nameof(UsePassphrase))]
    private string _keyScheme;

    [ObservableProperty] private bool _convertKey;

    [ObservableProperty] private string? _keyDescriptor;
    [ObservableProperty] private bool _autoMigrate;
    [ObservableProperty] private int _syncIntervalSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseWindowsAuth), nameof(UseEntraId), nameof(UseAnonymous))]
    private AuthMode _authMode;

    [ObservableProperty] private string? _anonymousName;

    [ObservableProperty] private string? _entraClientId;
    [ObservableProperty] private string? _entraTenantId;
    [ObservableProperty] private int _presenceTtlSeconds;
    [ObservableProperty] private int _presenceHeartbeatSeconds;
    [ObservableProperty] private bool _keepCredential;
    [ObservableProperty] private string? _testResult;
    [ObservableProperty] private string? _error;

    // Radio-button partners: only a check changes the mode. An uncheck is just WPF clearing the other button of the
    // pair and must not flip the setting (that made the dialog show and save the wrong storage mode). SPEC FR-26
    public bool IsLocal
    {
        get => !IsRemote;
        set { if (value) IsRemote = false; }
    }

    // ADR-0016, ADR-0018: "Not protected" – the key is stored unencrypted in the database (scheme name `sign-in`).
    public bool UseSignInKey
    {
        get => KeyScheme == SignInKeyProtector.SchemeName;
        set { if (value) KeyScheme = SignInKeyProtector.SchemeName; }
    }

    public bool UseDpapiNg
    {
        get => KeyScheme == DpapiNgKeyProtector.SchemeName;
        set { if (value) KeyScheme = DpapiNgKeyProtector.SchemeName; }
    }

    public bool UsePassphrase
    {
        get => KeyScheme == PassphraseKeyProtector.SchemeName;
        set { if (value) KeyScheme = PassphraseKeyProtector.SchemeName; }
    }

    public bool UseWindowsAuth
    {
        get => AuthMode == AuthMode.Windows;
        set { if (value) AuthMode = AuthMode.Windows; }
    }

    public bool UseEntraId
    {
        get => AuthMode == AuthMode.EntraId;
        set { if (value) AuthMode = AuthMode.EntraId; }
    }

    public bool UseAnonymous
    {
        get => AuthMode == AuthMode.Anonymous;
        set { if (value) AuthMode = AuthMode.Anonymous; }
    }

    private IServerDatabaseProvider? ServerProvider =>
        DatabaseProviders.Names.Contains(RemoteProvider, StringComparer.OrdinalIgnoreCase)
            ? DatabaseProviders.Get(RemoteProvider) as IServerDatabaseProvider
            : null;

    public bool SupportsFields => ServerProvider is not null;
    public bool ShowFields => SupportsFields && !EditRawConnectionString;
    public bool UsesSqlLogin => DbAuthentication == DatabaseAuthentication.SqlLogin;

    private bool _switchingEditor;

    partial void OnEditRawConnectionStringChanged(bool value)
    {
        if (ServerProvider is null || _switchingEditor) return;
        if (value)
        {
            ConnectionString = BuildFromFields();
        }
        else
        {
            LoadFields(ConnectionString);
        }
    }

    partial void OnRemoteProviderChanged(string value)
    {
        if (!SupportsFields) EditRawConnectionString = true;
    }

    /// <summary>The connection string that Test and Save use: the form fields, or the raw text when edited directly.</summary>
    public string? EffectiveConnectionString => ShowFields ? BuildFromFields() : ConnectionString;

    private string? BuildFromFields()
    {
        if (ServerProvider is not { } provider || string.IsNullOrWhiteSpace(DbServer)) return null;
        try
        {
            return provider.BuildConnectionString(new DatabaseServerConnection
            {
                Server = DbServer,
                Database = DbName ?? "",
                Authentication = DbAuthentication,
                UserName = DbUserName,
                Password = DbPassword,
                Encrypt = DbEncrypt,
                TrustServerCertificate = DbTrustServerCertificate,
            }, _baseConnectionString);
        }
        catch (ArgumentException)
        {
            return null; // the saved string is unparsable; Validate reports it
        }
    }

    private void LoadFields(string? connectionString)
    {
        if (ServerProvider is not { } provider || string.IsNullOrWhiteSpace(connectionString)) return;
        if (!provider.CanEditAsFields(connectionString))
        {
            // Something the form cannot show (e.g. another Entra method): keep it as typed instead of losing it.
            _switchingEditor = true;
            EditRawConnectionString = true;
            _switchingEditor = false;
            TestResult = "This connection string uses options the form cannot show, so it is edited as text.";
            return;
        }

        _baseConnectionString = connectionString;
        var fields = provider.ParseConnectionString(connectionString);
        DbServer = fields.Server;
        DbName = fields.Database;
        DbAuthentication = fields.Authentication;
        DbUserName = fields.UserName;
        DbPassword = fields.Password;
        DbEncrypt = fields.Encrypt;
        DbTrustServerCertificate = fields.TrustServerCertificate;
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        TestResult = "Connecting…";
        try
        {
            var connectionString = EffectiveConnectionString ?? "";
            var created = CreateDatabaseIfMissing && await EnsureDatabaseAsync(connectionString);
            var provider = DatabaseProviders.Get(RemoteProvider);
            await using var connection = provider.CreateConnection(connectionString);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await connection.OpenAsync(timeout.Token);
            TestResult = $"OK – {(created ? "created and " : "")}connected to {connection.DataSource} / {connection.Database}";
        }
        catch (Exception ex)
        {
            TestResult = $"Failed: {ex.Message}";
        }
    }

    /// <summary>Runs the one-time actions of Save that need the network. Returns false (with <see cref="Error"/>) on failure.</summary>
    public async Task<bool> PrepareSaveAsync()
    {
        if (!IsRemote || !CreateDatabaseIfMissing) return true;
        try
        {
            await EnsureDatabaseAsync(EffectiveConnectionString ?? "");
            return true;
        }
        catch (Exception ex)
        {
            Error = $"The database could not be created: {ex.Message} Untick \"Create the database\" to save anyway.";
            return false;
        }
    }

    private async Task<bool> EnsureDatabaseAsync(string connectionString)
    {
        if (ServerProvider is not { } provider) return false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        return await provider.EnsureDatabaseExistsAsync(connectionString, timeout.Token);
    }

    public bool Validate()
    {
        Error = null;
        if (IsRemote && ShowFields && string.IsNullOrWhiteSpace(DbServer)) Error = "Enter the database server name or IP address.";
        else if (IsRemote && ShowFields && string.IsNullOrWhiteSpace(DbName)) Error = "Enter the database name.";
        else if (IsRemote && ShowFields && UsesSqlLogin && string.IsNullOrWhiteSpace(DbUserName)) Error = "Enter the SQL Server login name.";
        else if (IsRemote && string.IsNullOrWhiteSpace(EffectiveConnectionString)) Error = "A valid connection string is required for a shared database.";
        else if (IsRemote && UseDpapiNg && string.IsNullOrWhiteSpace(KeyDescriptor))
            Error = "Enter a DPAPI-NG descriptor, e.g. SID=S-1-5-21-…, of the AD group that may read the vault.";
        else if (UseEntraId && string.IsNullOrWhiteSpace(EntraClientId)) Error = "Entra ID sign-in needs the app registration's client id.";
        else if (UseAnonymous && string.IsNullOrWhiteSpace(AnonymousName)) Error = "Enter your name so others can see who is connected.";
        else if (PresenceHeartbeatSeconds * 2 > PresenceTtlSeconds) Error = "Heartbeat must be at most half of the session timeout.";
        return Error is null;
    }

    public void Apply()
    {
        _settings.StorageMode = IsRemote ? StorageMode.Remote : StorageMode.Local;
        _settings.RemoteProvider = RemoteProvider;
        if (IsRemote)
        {
            // Stored DPAPI-protected only, never logged (it may hold a SQL login password). NFR-12
            SettingsStore.SetRemoteConnectionString(_settings, EffectiveConnectionString);
        }

        _settings.RemoteKeyScheme = KeyScheme;
        _settings.RemoteKeyConvert = IsRemote && ConvertKey;
        _settings.RemoteKeyDescriptor = string.IsNullOrWhiteSpace(KeyDescriptor) ? null : KeyDescriptor.Trim();
        _settings.RemoteAutoMigrate = AutoMigrate;
        _settings.RemoteSyncIntervalSeconds = Math.Max(10, SyncIntervalSeconds);
        _settings.AuthMode = AuthMode;
        _settings.AnonymousName = string.IsNullOrWhiteSpace(AnonymousName) ? null : AnonymousName.Trim();
        _settings.EntraClientId = string.IsNullOrWhiteSpace(EntraClientId) ? null : EntraClientId.Trim();
        _settings.EntraTenantId = string.IsNullOrWhiteSpace(EntraTenantId) ? "organizations" : EntraTenantId.Trim();
        _settings.PresenceTtlSeconds = PresenceTtlSeconds;
        _settings.PresenceHeartbeatSeconds = PresenceHeartbeatSeconds;
        _settings.KeepCredentialAfterLaunch = KeepCredential;
    }

    private static string? SafeGetConnectionString(HarborSettings settings)
    {
        try
        {
            return SettingsStore.GetRemoteConnectionString(settings);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null; // settings copied from another user/machine
        }
    }
}
