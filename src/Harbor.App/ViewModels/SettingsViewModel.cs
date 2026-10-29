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
        _useDpapiNg = settings.RemoteKeyScheme != PassphraseKeyProtector.SchemeName;
        _keyDescriptor = settings.RemoteKeyDescriptor;
        _autoMigrate = settings.RemoteAutoMigrate;
        _syncIntervalSeconds = settings.RemoteSyncIntervalSeconds;
        _useEntraId = settings.AuthMode == AuthMode.EntraId;
        _entraClientId = settings.EntraClientId;
        _entraTenantId = settings.EntraTenantId;
        _presenceTtlSeconds = settings.PresenceTtlSeconds;
        _presenceHeartbeatSeconds = settings.PresenceHeartbeatSeconds;
        _keepCredential = settings.KeepCredentialAfterLaunch;
    }

    public IEnumerable<string> Providers => DatabaseProviders.Names.Where(n => n != SqliteProvider.ProviderName);
    public string AboutText => $"{AppInfo.ProductName} · {AppInfo.Description}";
    public string DataDirectory => AppPaths.DataDirectory + (AppPaths.IsPortable ? "  (portable mode)" : "");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLocal))]
    private bool _isRemote;

    [ObservableProperty] private string _remoteProvider;
    [ObservableProperty] private string? _connectionString;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsePassphrase))]
    private bool _useDpapiNg;

    [ObservableProperty] private string? _keyDescriptor;
    [ObservableProperty] private bool _autoMigrate;
    [ObservableProperty] private int _syncIntervalSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseWindowsAuth))]
    private bool _useEntraId;

    [ObservableProperty] private string? _entraClientId;
    [ObservableProperty] private string? _entraTenantId;
    [ObservableProperty] private int _presenceTtlSeconds;
    [ObservableProperty] private int _presenceHeartbeatSeconds;
    [ObservableProperty] private bool _keepCredential;
    [ObservableProperty] private string? _testResult;
    [ObservableProperty] private string? _error;

    public bool IsLocal { get => !IsRemote; set => IsRemote = !value; }
    public bool UsePassphrase { get => !UseDpapiNg; set => UseDpapiNg = !value; }
    public bool UseWindowsAuth { get => !UseEntraId; set => UseEntraId = !value; }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        TestResult = "Connecting…";
        try
        {
            var provider = DatabaseProviders.Get(RemoteProvider);
            await using var connection = provider.CreateConnection(ConnectionString ?? "");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await connection.OpenAsync(timeout.Token);
            TestResult = $"OK – connected to {connection.DataSource} / {connection.Database}";
        }
        catch (Exception ex)
        {
            TestResult = $"Failed: {ex.Message}";
        }
    }

    public bool Validate()
    {
        Error = null;
        if (IsRemote && string.IsNullOrWhiteSpace(ConnectionString)) Error = "A connection string is required for a shared database.";
        else if (IsRemote && UseDpapiNg && string.IsNullOrWhiteSpace(KeyDescriptor))
            Error = "Enter a DPAPI-NG descriptor, e.g. SID=S-1-5-21-…, of the AD group that may read the vault.";
        else if (UseEntraId && string.IsNullOrWhiteSpace(EntraClientId)) Error = "Entra ID sign-in needs the app registration's client id.";
        else if (PresenceHeartbeatSeconds * 2 > PresenceTtlSeconds) Error = "Heartbeat must be at most half of the session timeout.";
        return Error is null;
    }

    public void Apply()
    {
        _settings.StorageMode = IsRemote ? StorageMode.Remote : StorageMode.Local;
        _settings.RemoteProvider = RemoteProvider;
        SettingsStore.SetRemoteConnectionString(_settings, ConnectionString);
        _settings.RemoteKeyScheme = UseDpapiNg ? DpapiNgKeyProtector.SchemeName : PassphraseKeyProtector.SchemeName;
        _settings.RemoteKeyDescriptor = string.IsNullOrWhiteSpace(KeyDescriptor) ? null : KeyDescriptor.Trim();
        _settings.RemoteAutoMigrate = AutoMigrate;
        _settings.RemoteSyncIntervalSeconds = Math.Max(10, SyncIntervalSeconds);
        _settings.AuthMode = UseEntraId ? AuthMode.EntraId : AuthMode.Windows;
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
