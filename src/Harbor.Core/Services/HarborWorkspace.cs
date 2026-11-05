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
/// See ADR-0007. In remote mode entries can also be kept in this PC's local vault only, and moved between it and the
/// shared database (SPEC FR-29, ADR-0038).
/// </summary>
public sealed class HarborWorkspace : IDisposable
{
    internal const string WatermarkKey = "sync.watermark";
    private static readonly TimeSpan WatermarkOverlap = TimeSpan.FromMinutes(5);

    private readonly IConnectionStore _local;
    private readonly IConnectionStore? _remote;
    private readonly IConnectionStore? _localVault;
    private readonly IKeyProtector _localProtector;
    private readonly IKeyProtector? _remoteProtectorForNewKey;
    private readonly IKeyProtectorResolver _resolver;
    private readonly bool _migrateRemote;
    private readonly SemaphoreSlim _remoteLock = new(1, 1);
    private SecretCipher? _cipher;
    private SecretCipher? _localVaultCipher;
    private bool _remoteKeyVerified;

    private HarborWorkspace(UserIdentity user, IConnectionStore local, IConnectionStore? remote, IKeyProtector localProtector,
        IKeyProtector? remoteProtectorForNewKey, IKeyProtectorResolver resolver, bool migrateRemote, IConnectionStore? localVault)
    {
        User = user;
        _local = local;
        _remote = remote;
        _localProtector = localProtector;
        _remoteProtectorForNewKey = remoteProtectorForNewKey;
        _resolver = resolver;
        _migrateRemote = migrateRemote;
        _localVault = localVault;
    }

    public static HarborWorkspace CreateLocal(UserIdentity user, IConnectionStore store, IKeyProtector localProtector,
        IKeyProtectorResolver resolver) =>
        new(user, store, null, localProtector, null, resolver, migrateRemote: false, localVault: null);

    /// <summary>
    /// Remote mode. <paramref name="localVault"/> (the local-mode vault file of this PC) holds entries kept on this PC only
    /// (SPEC FR-29, ADR-0038); <c>null</c> disables them.
    /// </summary>
    public static HarborWorkspace CreateRemote(UserIdentity user, IConnectionStore cache, IConnectionStore remote,
        IKeyProtector localProtector, IKeyProtector remoteProtectorForNewKey, IKeyProtectorResolver resolver, bool migrateRemote,
        IConnectionStore? localVault = null) =>
        new(user, cache, remote, localProtector, remoteProtectorForNewKey, resolver, migrateRemote, localVault);

    public UserIdentity User { get; }
    public bool IsRemote => _remote is not null;

    /// <summary>Local mode is always online. Remote mode is online while the shared database answers.</summary>
    public bool IsOnline { get; private set; }

    /// <summary>Writes are allowed in local mode and in remote mode while online.</summary>
    public bool CanWrite => !IsRemote || IsOnline;

    /// <summary>
    /// Remote mode with this PC's local vault open: entries can be kept on this PC only and moved between it and the shared
    /// database. SPEC FR-29
    /// </summary>
    public bool SupportsLocalEntries => _localVaultCipher is not null;

    /// <summary>Why <see cref="SupportsLocalEntries"/> is false although a local vault was given (e.g. it cannot be decrypted here).</summary>
    public Exception? LocalVaultError { get; private set; }

    /// <summary>Whether <paramref name="entry"/> can be changed or deleted now: local-only entries also while offline. SPEC FR-29</summary>
    public bool CanWriteEntry(ConnectionEntry entry) => entry.IsLocalOnly ? SupportsLocalEntries : CanWrite;

    /// <summary>
    /// Remote mode: on the next successful connection, re-protect the existing shared vault key with the protector for
    /// new keys (keeping the key itself). SPEC FR-28, ADR-0016
    /// </summary>
    public bool ConvertRemoteKeyProtection { get; set; }

    /// <summary>True once <see cref="ConvertRemoteKeyProtection"/> has been carried out (or was not needed).</summary>
    public bool RemoteKeyConverted { get; private set; }

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

        await OpenLocalVaultAsync(cancellationToken).ConfigureAwait(false);

        // Start from the key cached on this machine: shown offline, and SyncAsync then needs no passphrase or DC
        // when the shared key is unchanged (ADR-0006).
        Exception? cacheError = null;
        try
        {
            _cipher = await VaultKeyManager.TryOpenAsync(_local, _resolver, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            cacheError = ex; // e.g. a cache copied from another user/machine; replaced once online
        }

        await SyncAsync(cancellationToken).ConfigureAwait(false);

        if (_cipher is null)
        {
            if (cacheError is not null)
            {
                throw new WorkspaceOfflineException("The offline copy cannot be decrypted on this machine/user.", cacheError);
            }

            throw new WorkspaceOfflineException(
                "The shared database is unreachable and there is no offline copy on this machine yet. " +
                $"Connect once while online. ({LastRemoteError?.Message})", LastRemoteError);
        }
    }

    /// <summary>
    /// Opens this PC's local vault for local-only entries (SPEC FR-29). A vault that cannot be opened (e.g. copied from
    /// another user or machine) only disables local-only entries; the shared database still opens.
    /// </summary>
    private async Task OpenLocalVaultAsync(CancellationToken cancellationToken)
    {
        if (_localVault is null)
        {
            return;
        }

        try
        {
            await _localVault.InitializeAsync(cancellationToken).ConfigureAwait(false);
            _localVaultCipher = await VaultKeyManager.OpenOrCreateAsync(_localVault, _localProtector, _resolver, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LocalVaultError = ex;
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

                var remoteKey = await _remote.GetVaultKeyAsync(VaultKeyRecord.PrimarySlot, cancellationToken).ConfigureAwait(false);
                if (remoteKey is null || _cipher?.KeyId != remoteKey.KeyId)
                {
                    // Only unwrap the shared key (passphrase prompt / DC) when this machine has not cached it yet.
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
                }

                _remoteKeyVerified = true;
            }

            if (ConvertRemoteKeyProtection && !RemoteKeyConverted && _cipher is not null)
            {
                var current = await _remote.GetVaultKeyAsync(VaultKeyRecord.PrimarySlot, cancellationToken).ConfigureAwait(false);
                if (current is not null && current.KeyId == _cipher.KeyId &&
                    !string.Equals(current.Scheme, _remoteProtectorForNewKey!.Scheme, StringComparison.OrdinalIgnoreCase))
                {
                    // Same key, new wrapping: other clients' cached copies stay valid.
                    await VaultKeyManager.CacheAsync(_remote, _cipher, _remoteProtectorForNewKey, cancellationToken).ConfigureAwait(false);
                }

                RemoteKeyConverted = true;
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

    /// <summary>
    /// All entries: in remote mode the cached shared ones followed by this PC's local-only ones (marked
    /// <see cref="ConnectionEntry.IsLocalOnly"/>, SPEC FR-29).
    /// </summary>
    public async Task<IReadOnlyList<ConnectionEntry>> ListAsync(CancellationToken cancellationToken = default)
    {
        var entries = await _local.ListAsync(includeDeleted: false, cancellationToken).ConfigureAwait(false);
        if (!SupportsLocalEntries)
        {
            return entries;
        }

        var localOnly = await _localVault!.ListAsync(includeDeleted: false, cancellationToken).ConfigureAwait(false);
        return [.. entries, .. localOnly.Select(MarkLocal)];
    }

    private static ConnectionEntry MarkLocal(ConnectionEntry entry) => entry with { IsLocalOnly = true };

    /// <summary>
    /// Saves <paramref name="entry"/> with its accounts. <paramref name="password"/> applies to the default account;
    /// <paramref name="accountPasswords"/> to additional accounts by id (missing = keep). SPEC FR-04, FR-09
    /// </summary>
    /// <remarks>
    /// In remote mode an entry with <see cref="ConnectionEntry.IsLocalOnly"/> is saved to this PC's local vault only,
    /// also while the shared database is offline (SPEC FR-29).
    /// </remarks>
    public async Task<ConnectionEntry> SaveAsync(ConnectionEntry entry, SecretUpdate password,
        IReadOnlyDictionary<Guid, SecretUpdate>? accountPasswords = null, CancellationToken cancellationToken = default)
    {
        var cipher = CipherFor(entry);
        var toStore = entry with
        {
            PasswordCipher = Apply(cipher, password, entry.PasswordCipher, entry.Id),
            Accounts = entry.Accounts.Select(a => a with
            {
                PasswordCipher = Apply(cipher, accountPasswords?.GetValueOrDefault(a.Id) ?? SecretUpdate.Keep, a.PasswordCipher, a.Id),
            }).ToList(),
        };

        if (IsLocalOnlyIn(entry))
        {
            return MarkLocal(await _localVault!.SaveAsync(toStore, User.UserName, cancellationToken).ConfigureAwait(false));
        }

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
        if (IsLocalOnlyIn(entry))
        {
            await _localVault!.DeleteAsync(entry.Id, entry.Version, User.UserName, cancellationToken).ConfigureAwait(false);
            return;
        }

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

    /// <summary>
    /// Decrypts the password of the entry's default account, or of its additional account <paramref name="accountId"/>.
    /// Only call right before use (launch / copy).
    /// </summary>
    public string? RevealPassword(ConnectionEntry entry, Guid? accountId = null)
    {
        // Each cipher is bound to its own row: the entry id for the default account, the account id otherwise (ADR-0019).
        if (accountId is not null)
        {
            var account = entry.FindAccount(accountId) ?? throw new InvalidOperationException("The account no longer exists.");
            return account.PasswordCipher is { Length: > 0 } a ? CipherFor(entry).Decrypt(a, account.Id) : null;
        }

        return entry.PasswordCipher is { Length: > 0 } c ? CipherFor(entry).Decrypt(c, entry.Id) : null;
    }

    /// <summary>
    /// Moves <paramref name="entry"/> from the shared database to this PC's local vault (<paramref name="toLocal"/>) or back,
    /// with its accounts and passwords. Needs the shared database online. The moved entry gets new ids (the source keeps a
    /// tombstone under the old ones, so caches learn it is gone) and its passwords are re-encrypted with the target vault's
    /// key, bound to the new ids. Returns the stored entry. SPEC FR-29, ADR-0038
    /// </summary>
    /// <exception cref="WorkspaceOfflineException">The shared database is offline.</exception>
    /// <exception cref="ConcurrencyConflictException">The entry was changed or deleted in the meantime; nothing was moved.</exception>
    public async Task<ConnectionEntry> MoveAsync(ConnectionEntry entry, bool toLocal, CancellationToken cancellationToken = default)
    {
        if (!SupportsLocalEntries)
        {
            throw new InvalidOperationException("Entries can only be moved in shared database mode with a local vault on this PC.");
        }

        if (entry.IsLocalOnly == toLocal)
        {
            return entry;
        }

        if (!IsOnline)
        {
            throw new WorkspaceOfflineException(
                "The shared database is offline. Entries can be moved once it is reachable again.", LastRemoteError);
        }

        var copy = Recrypt(entry, CipherFor(entry), toLocal ? _localVaultCipher! : Cipher) with { IsLocalOnly = toLocal };
        if (toLocal)
        {
            // Local copy first, then the shared tombstone; if the shared delete fails, the local copy is withdrawn again.
            var stored = await _localVault!.SaveAsync(copy, User.UserName, cancellationToken).ConfigureAwait(false);
            try
            {
                await DeleteAsync(entry, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                await _localVault.DeleteAsync(stored.Id, stored.Version, User.UserName, CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            return MarkLocal(stored);
        }

        var shared = await OnRemoteAsync(r => r.SaveAsync(copy, User.UserName, cancellationToken)).ConfigureAwait(false);
        await _local.ApplyReplicaAsync([shared], cancellationToken).ConfigureAwait(false);
        try
        {
            await _localVault!.DeleteAsync(entry.Id, entry.Version, User.UserName, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await DeleteAsync(shared, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return shared;
    }

    /// <summary>
    /// A new, unsaved copy of <paramref name="entry"/> with new ids whose passwords are decrypted with <paramref name="from"/>
    /// and encrypted with <paramref name="to"/>, bound to the new ids (NFR-11). The plaintext only lives in memory.
    /// </summary>
    private static ConnectionEntry Recrypt(ConnectionEntry entry, SecretCipher from, SecretCipher to)
    {
        var id = Guid.NewGuid();
        return entry with
        {
            Id = id,
            Version = 0,
            IsDeleted = false,
            PasswordCipher = entry.PasswordCipher is { Length: > 0 } c ? to.Encrypt(from.Decrypt(c, entry.Id), id) : null,
            Accounts = entry.Accounts.Select(a =>
            {
                var accountId = Guid.NewGuid();
                return a with
                {
                    Id = accountId,
                    PasswordCipher = a.PasswordCipher is { Length: > 0 } ac ? to.Encrypt(from.Decrypt(ac, a.Id), accountId) : null,
                };
            }).ToList(),
        };
    }

    private static byte[]? Apply(SecretCipher cipher, SecretUpdate update, byte[]? current, Guid boundTo) => update.Mode switch
    {
        SecretUpdateMode.Clear => null,
        SecretUpdateMode.Set when string.IsNullOrEmpty(update.Value) => null,
        SecretUpdateMode.Set => cipher.Encrypt(update.Value!, boundTo),
        _ => current,
    };

    private SecretCipher Cipher => _cipher ?? throw new InvalidOperationException("Workspace is not open.");

    /// <summary>Whether <paramref name="entry"/> lives in this PC's local vault next to a shared database (SPEC FR-29).</summary>
    private bool IsLocalOnlyIn(ConnectionEntry entry)
    {
        if (!entry.IsLocalOnly || _remote is null)
        {
            return false;
        }

        return SupportsLocalEntries
            ? true
            : throw new InvalidOperationException("This PC's local vault is not available.", LocalVaultError);
    }

    /// <summary>The key of the vault that holds <paramref name="entry"/>.</summary>
    private SecretCipher CipherFor(ConnectionEntry entry) => IsLocalOnlyIn(entry) ? _localVaultCipher! : Cipher;

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
        _localVaultCipher?.Dispose();
        _remoteLock.Dispose();
    }
}

public class WorkspaceOfflineException(string message, Exception? inner = null) : Exception(message, inner);
