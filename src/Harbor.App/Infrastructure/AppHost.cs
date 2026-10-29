using Harbor.Core;
using Harbor.Core.Identity;
using Harbor.Core.Rdp;
using Harbor.Core.Security;
using Harbor.Core.Services;
using Harbor.Core.Storage;
using Harbor.Data;
using Harbor.Data.Providers;
using Harbor.Windows.Identity;
using Harbor.Windows.Rdp;
using Harbor.Windows.Security;

namespace Harbor.App.Infrastructure;

/// <summary>Composition root: turns settings into a signed-in user, an open workspace and services.</summary>
public sealed class AppHost : IDisposable
{
    private AppHost(HarborSettings settings, UserIdentity user, HarborWorkspace workspace, SessionPresenceService presence,
        IRdpLauncher launcher)
    {
        Settings = settings;
        User = user;
        Workspace = workspace;
        Presence = presence;
        Launcher = launcher;
    }

    public HarborSettings Settings { get; }
    public UserIdentity User { get; }
    public HarborWorkspace Workspace { get; }
    public SessionPresenceService Presence { get; }
    public IRdpLauncher Launcher { get; }

    public static IIdentityProvider CreateIdentityProvider(HarborSettings settings, Func<nint> parentWindow) => settings.AuthMode switch
    {
        AuthMode.EntraId when !string.IsNullOrWhiteSpace(settings.EntraClientId) =>
            new EntraIdIdentityProvider(settings.EntraClientId, settings.EntraTenantId, parentWindow),
        AuthMode.Anonymous => new AnonymousIdentityProvider(settings.AnonymousName ?? ""), // SPEC FR-44
        _ => new WindowsIdentityProvider(),
    };

    public static async Task<AppHost> StartAsync(HarborSettings settings, Func<nint> parentWindow,
        Func<bool, string?> passphrasePrompt, CancellationToken cancellationToken = default)
    {
        var user = await CreateIdentityProvider(settings, parentWindow).SignInAsync(allowInteractive: true, cancellationToken)
            .ConfigureAwait(false);
        Log.Info($"Signed in as {user.UserName} via {user.Source}");

        var local = DpapiKeyProtector.UserAndMachine;
        var passphrase = new PassphraseKeyProtector(() => passphrasePrompt(false));
        var newSharedPassphrase = new PassphraseKeyProtector(() => passphrasePrompt(true));
        var dpapiNg = new DpapiNgKeyProtector(settings.RemoteKeyDescriptor);
        var signIn = new SignInKeyProtector(); // ADR-0016, ADR-0018
        var resolver = new KeyProtectorResolver([local, DpapiKeyProtector.User, passphrase, dpapiNg, signIn]);

        HarborWorkspace workspace;
        IPresenceStore? presenceStore = null;
        var remoteConnectionString = settings.StorageMode == StorageMode.Remote
            ? SettingsStore.GetRemoteConnectionString(settings)
            : null;

        if (remoteConnectionString is null)
        {
            var store = new SqlHarborStore(DatabaseProviders.Sqlite, SqliteProvider.ConnectionStringForFile(AppPaths.LocalDatabase));
            workspace = HarborWorkspace.CreateLocal(user, store, local, resolver);
        }
        else
        {
            var cache = new SqlHarborStore(DatabaseProviders.Sqlite,
                SqliteProvider.ConnectionStringForFile(AppPaths.CacheDatabase(settings.RemoteProvider, remoteConnectionString)));
            var remote = new SqlHarborStore(DatabaseProviders.Get(settings.RemoteProvider), remoteConnectionString);
            IKeyProtector forNewKey = settings.RemoteKeyScheme switch
            {
                PassphraseKeyProtector.SchemeName => newSharedPassphrase,
                DpapiNgKeyProtector.SchemeName => dpapiNg,
                _ => signIn,
            };
            workspace = HarborWorkspace.CreateRemote(user, cache, remote, local, forNewKey, resolver, settings.RemoteAutoMigrate);
            workspace.ConvertRemoteKeyProtection = settings.RemoteKeyConvert;
            presenceStore = remote;
        }

        System.IO.Directory.CreateDirectory(AppPaths.DataDirectory);
        await workspace.OpenAsync(cancellationToken).ConfigureAwait(false);
        Log.Info($"Workspace open: {(workspace.IsRemote ? "remote" : "local")}, online={workspace.IsOnline}");

        var presence = new SessionPresenceService(presenceStore, PresenceOptions.FromSettings(settings), Environment.MachineName,
            ex => Log.Error("Presence update failed", ex));
        var launcher = new MstscLauncher(AppPaths.TempDirectory, settings.KeepCredentialAfterLaunch,
            ex => Log.Error("RDP launch cleanup failed", ex));

        if (presence.IsEnabled)
        {
            _ = presence.PurgeAsync(CancellationToken.None);
        }

        CleanupStaleTempFiles();
        return new AppHost(settings, user, workspace, presence, launcher);
    }

    /// <summary>Removes .rdp files left behind if the app was killed during a launch.</summary>
    private static void CleanupStaleTempFiles()
    {
        try
        {
            if (!System.IO.Directory.Exists(AppPaths.TempDirectory))
            {
                return;
            }

            foreach (var file in System.IO.Directory.EnumerateFiles(AppPaths.TempDirectory, "*.rdp"))
            {
                if (System.IO.File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddMinutes(-5))
                {
                    System.IO.File.Delete(file);
                }
            }
        }
        catch (System.IO.IOException)
        {
        }
    }

    public void Dispose() => Workspace.Dispose();
}
