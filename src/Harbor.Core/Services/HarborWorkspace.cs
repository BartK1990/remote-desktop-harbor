using System.Data.Common;
using System.Globalization;
using System.Net.Sockets;
using Harbor.Core.Identity;
using Harbor.Core.Model;
using Harbor.Core.Security;
using Harbor.Core.Storage;

namespace Harbor.Core.Services;

/// <summary>
/// The app's single entry point to stored connections. In <see cref="StorageMode.Local"/> it works on one
/// SQLite file. In <see cref="StorageMode.Remote"/> the shared database is the source of truth and a local
/// SQLite cache serves reads and keeps working (read-only) while the shared database is unreachable.
/// See ADR-0007.
/// </summary>
public sealed class HarborWorkspace : IDisposable
{
    internal const string WatermarkKey = "sync.watermark";
    private static readonly TimeSpan WatermarkOverlap = TimeSpan.FromMinutes(5);

    private readonly IConnectionStore _local;
    private readonly IConnectionStore? _remote;
    private readonly IKeyProtector _localProtector;
    private readonly IKeyProtector? _remoteProtectorForNewKey;
    private readonly IKeyProtectorResolver _resolver;
    private readonly bool _migrateRemote;
    private readonly SemaphoreSlim _remoteLock = new(1, 1);
    private SecretCipher? _cipher;
    private bool _remoteKeyVerified;

    private HarborWorkspace(UserIdentity user, IConnectionStore local, IConnectionStore? remote, IKeyProtector localProtector,
        IKeyProtector? remoteProtectorForNewKey, IKeyProtectorResolver resolver, bool migrateRemote)
    {
        User = user;
        _local = local;
        _remote = remote;
        _localProtector = localProtector;
        _remoteProtectorForNewKey = remoteProtectorForNewKey;
        _resolver = resolver;
        _migrateRemote = migrateRemote;
    }

    public static HarborWorkspace CreateLocal(UserIdentity user, IConnectionStore store, IKeyProtector localProtector,
        IKeyProtectorResolver resolver) =>
        new(user, store, null, localProtector, null, resolver, migrateRemote: false);

    public static HarborWorkspace CreateRemote(UserIdentity user, IConnectionStore cache, IConnectionStore remote,
        IKeyProtector localProtector, IKeyProtector remoteProtectorForNewKey, IKeyProtectorResolver resolver, bool migrateRemote) =>
        new(user, cache, remote, localProtector, remoteProtectorForNewKey, resolver, migrateRemote);

    public UserIdentity User { get; }
    public bool IsRemote => _remote is not null;

    /// <summary>Local mode is always online. Remote mode is online while the shared database answers.</summary>
    public bool IsOnline { get; private set; }

    /// <summary>Writes are allowed in local mode and in remote mode while online.</summary>
    public bool CanWrite => !IsRemote || IsOnline;

    public DateTimeOffset? LastSyncUtc { get; private set; }
    public Exception? LastRemoteError { get; private set; }

    /// <summary>Raised (possibly on a background thread) when online state or sync time changes.</summary>
    public event EventHandler? StateChanged;

    public async Task OpenAsync(CancellationToken cancellationToken = default)
    {
        await _local.InitializeAsync(cancellationToken).ConfigureAwait(false);

        if (_remote is null)
        {
            _cipher = await VaultKeyManager.OpenOrCreateAsync(_local, _localProtector, _resolver, cancellationToken).ConfigureAwait(false);
            IsOnline = true;
            return;
        }

        await SyncAsync(cancellationToken).ConfigureAwait(false);

        if (_cipher is null)
        {
            try
            {
                _cipher = await VaultKeyManager.TryOpenAsync(_local, _resolver, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new WorkspaceOfflineException("The offline copy cannot be decrypted on this machine/user.", ex);
            }

            if (_cipher is null)
            {
                throw new WorkspaceOfflineException(
                    "The shared database is unreachable and there is no offline copy on this machine yet. " +
                    $"Connect once while online. ({LastRemoteError?.Message})", LastRemoteError);
            }
        }
    }

    /// <summary>
    /// Remote mode: (re)connects if needed and pulls changes into the cache. Never throws for connectivity
    /// problems; check <see cref="IsOnline"/> / <see cref="LastRemoteError"/>.
    /// </summary>
    public async Task SyncAsync(CancellationToken cancellationToken = default)
    {
        if (_remote is null)
        {
            return;
        }

        await _remoteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_remoteKeyVerified)
            {
                if (_migrateRemote)
                {
                    await _remote.InitializeAsync(cancellationToken).ConfigureAwait(false);
                }

                var cipher = await VaultKeyManager.OpenOrCreateAsync(_remote, _remoteProtectorForNewKey!, _resolver, cancellationToken)
                    .ConfigureAwait(false);
                var cached = await _local.GetVaultKeyAsync(VaultKeyRecord.PrimarySlot, cancellationToken).ConfigureAwait(false);
                if (cached?.KeyId != cipher.KeyId)
                {
                    await VaultKeyManager.CacheAsync(_local, cipher, _localProtector, cancellationToken).ConfigureAwait(false);
                }

                if (_cipher?.KeyId == cipher.KeyId)
                {
                    cipher.Dispose(); // same key as the cached one already in use
                }
                else
                {
                    // Not disposing the previous cipher: a UI call may still be using it; it is unreachable from now on.
                    _cipher = cipher;
                }

                _remoteKeyVerified = true;
            }

            await PullAsync(cancellationToken).ConfigureAwait(false);
            SetOnline();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            SetOffline(ex);
        }
        finally
        {
            _remoteLock.Release();
        }
    }

    public Task<IReadOnlyList<ConnectionEntry>> ListAsync(CancellationToken cancellationToken = default) =>
        _local.ListAsync(includeDeleted: false, cancellationToken);

    public async Task<ConnectionEntry> SaveAsync(ConnectionEntry entry, SecretUpdate password, CancellationToken cancellationToken = default)
    {
        var cipher = Cipher;
        var toStore = password.Mode switch
        {
            SecretUpdateMode.Clear => entry with { PasswordCipher = null },
            SecretUpdateMode.Set when string.IsNullOrEmpty(password.Value) => entry with { PasswordCipher = null },
            SecretUpdateMode.Set => entry with { PasswordCipher = cipher.Encrypt(password.Value!, entry.Id) },
            _ => entry,
        };

        if (_remote is null)
        {
            return await _local.SaveAsync(toStore, User.UserName, cancellationToken).ConfigureAwait(false);
        }

        var stored = await OnRemoteAsync(r => r.SaveAsync(toStore, User.UserName, cancellationToken)).ConfigureAwait(false);
        await _local.ApplyReplicaAsync([stored], cancellationToken).ConfigureAwait(false);
        return stored;
    }

    public async Task DeleteAsync(ConnectionEntry entry, CancellationToken cancellationToken = default)
    {
        if (_remote is null)
        {
            await _local.DeleteAsync(entry.Id, entry.Version, User.UserName, cancellationToken).ConfigureAwait(false);
            return;
        }

        var tombstone = await OnRemoteAsync(async r =>
        {
            await r.DeleteAsync(entry.Id, entry.Version, User.UserName, cancellationToken).ConfigureAwait(false);
            return await r.GetAsync(entry.Id, cancellationToken).ConfigureAwait(false);
        }).ConfigureAwait(false);
        if (tombstone is not null)
        {
            await _local.ApplyReplicaAsync([tombstone], cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Decrypts the entry's password. Only call right before use (launch / copy).</summary>
    public string? RevealPassword(ConnectionEntry entry) =>
        entry.PasswordCipher is { Length: > 0 } c ? Cipher.Decrypt(c, entry.Id) : null;

    private SecretCipher Cipher => _cipher ?? throw new InvalidOperationException("Workspace is not open.");

    private async Task<T> OnRemoteAsync<T>(Func<IConnectionStore, Task<T>> action)
    {
        if (!IsOnline)
        {
            throw new WorkspaceOfflineException(
                "The shared database is offline. Changes are disabled until it is reachable again.", LastRemoteError);
        }

        try
        {
            return await action(_remote!).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsConnectivityFailure(ex))
        {
            SetOffline(ex);
            throw new WorkspaceOfflineException("Lost connection to the shared database. The change was not saved.", ex);
        }
    }

    private async Task PullAsync(CancellationToken cancellationToken)
    {
        var watermark = await _local.GetMetaAsync(WatermarkKey, cancellationToken).ConfigureAwait(false);
        var since = watermark is null
            ? DateTimeOffset.UnixEpoch
            : DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(watermark, CultureInfo.InvariantCulture)) - WatermarkOverlap;

        var changes = await _remote!.ListChangedSinceAsync(since, cancellationToken).ConfigureAwait(false);
        if (changes.Count > 0)
        {
            await _local.ApplyReplicaAsync(changes, cancellationToken).ConfigureAwait(false);
            var max = changes.Max(c => c.UpdatedUtc).ToUnixTimeMilliseconds();
            await _local.SetMetaAsync(WatermarkKey, max.ToString(CultureInfo.InvariantCulture), cancellationToken).ConfigureAwait(false);
        }

        LastSyncUtc = DateTimeOffset.UtcNow;
    }

    internal static bool IsConnectivityFailure(Exception ex) =>
        ex is DbException or TimeoutException or SocketException or IOException
        || (ex.InnerException is { } inner && IsConnectivityFailure(inner));

    private void SetOnline()
    {
        LastRemoteError = null;
        IsOnline = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetOffline(Exception ex)
    {
        // The verified key stays loaded: the shared key does not change between reconnects, and re-unwrapping
        // could mean prompting for a passphrase again.
        LastRemoteError = ex;
        var changed = IsOnline;
        IsOnline = false;
        if (changed || LastSyncUtc is null)
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        _cipher?.Dispose();
        _remoteLock.Dispose();
    }
}

public class WorkspaceOfflineException(string message, Exception? inner = null) : Exception(message, inner);
