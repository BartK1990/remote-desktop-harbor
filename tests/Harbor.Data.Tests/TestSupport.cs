using System.Data.Common;
using Harbor.Core.Identity;
using Harbor.Core.Model;
using Harbor.Core.Security;
using Harbor.Core.Storage;
using Harbor.Data;
using Harbor.Data.Providers;
using Microsoft.Data.Sqlite;

namespace Harbor.Data.Tests;

internal sealed class TempDb : IDisposable
{
    public TempDb()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"harbor-test-{Guid.NewGuid():N}.db");
        Store = new SqlHarborStore(DatabaseProviders.Sqlite, SqliteProvider.ConnectionStringForFile(Path));
    }

    public string Path { get; }
    public SqlHarborStore Store { get; }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            try { File.Delete(Path + suffix); } catch (IOException) { }
        }
    }
}

/// <summary>Stand-in for DPAPI on non-Windows test agents.</summary>
internal sealed class TestKeyProtector(string scheme, string secret) : IKeyProtector
{
    private readonly PassphraseKeyProtector _inner = new(() => secret, iterations: 1000);
    public string Scheme => scheme;
    public byte[] Protect(ReadOnlySpan<byte> key) => _inner.Protect(key);
    public byte[] Unprotect(ReadOnlySpan<byte> protectedKey) => _inner.Unprotect(protectedKey);
}

internal sealed class FakeDbException(string message) : DbException(message);

/// <summary>Remote store whose connectivity can be switched off.</summary>
internal sealed class SwitchableStore(SqlHarborStore inner) : IConnectionStore, IPresenceStore
{
    public bool Offline { get; set; }

    private T Guard<T>(Func<T> f) => Offline ? throw new FakeDbException("network down") : f();

    public Task InitializeAsync(CancellationToken ct = default) => Guard(() => inner.InitializeAsync(ct));
    public Task<IReadOnlyList<ConnectionEntry>> ListAsync(bool includeDeleted = false, CancellationToken ct = default) => Guard(() => inner.ListAsync(includeDeleted, ct));
    public Task<IReadOnlyList<ConnectionEntry>> ListChangedSinceAsync(DateTimeOffset since, CancellationToken ct = default) => Guard(() => inner.ListChangedSinceAsync(since, ct));
    public Task<ConnectionEntry?> GetAsync(Guid id, CancellationToken ct = default) => Guard(() => inner.GetAsync(id, ct));
    public Task<ConnectionEntry> SaveAsync(ConnectionEntry entry, string modifiedBy, CancellationToken ct = default) => Guard(() => inner.SaveAsync(entry, modifiedBy, ct));
    public Task DeleteAsync(Guid id, long expectedVersion, string deletedBy, CancellationToken ct = default) => Guard(() => inner.DeleteAsync(id, expectedVersion, deletedBy, ct));
    public Task ApplyReplicaAsync(IEnumerable<ConnectionEntry> entries, CancellationToken ct = default) => Guard(() => inner.ApplyReplicaAsync(entries, ct));
    public Task<VaultKeyRecord?> GetVaultKeyAsync(string slot, CancellationToken ct = default) => Guard(() => inner.GetVaultKeyAsync(slot, ct));
    public Task<bool> TryAddVaultKeyAsync(VaultKeyRecord record, CancellationToken ct = default) => Guard(() => inner.TryAddVaultKeyAsync(record, ct));
    public Task ReplaceVaultKeyAsync(VaultKeyRecord record, CancellationToken ct = default) => Guard(() => inner.ReplaceVaultKeyAsync(record, ct));
    public Task<string?> GetMetaAsync(string key, CancellationToken ct = default) => Guard(() => inner.GetMetaAsync(key, ct));
    public Task SetMetaAsync(string key, string value, CancellationToken ct = default) => Guard(() => inner.SetMetaAsync(key, value, ct));
    public Task<ActiveSession> StartSessionAsync(Guid sessionId, Guid connectionId, UserIdentity user, string machineName, TimeSpan ttl, CancellationToken ct = default) => Guard(() => inner.StartSessionAsync(sessionId, connectionId, user, machineName, ttl, ct));
    public Task<bool> HeartbeatAsync(Guid sessionId, TimeSpan ttl, CancellationToken ct = default) => Guard(() => inner.HeartbeatAsync(sessionId, ttl, ct));
    public Task EndSessionAsync(Guid sessionId, CancellationToken ct = default) => Guard(() => inner.EndSessionAsync(sessionId, ct));
    public Task<IReadOnlyList<ActiveSession>> ListActiveAsync(CancellationToken ct = default) => Guard(() => inner.ListActiveAsync(ct));
    public Task<int> PurgeExpiredAsync(TimeSpan olderThan, CancellationToken ct = default) => Guard(() => inner.PurgeExpiredAsync(olderThan, ct));
}

internal static class Users
{
    public static readonly UserIdentity Alice = new("CONTOSO\\alice", "Alice Smith", "alice@contoso.com", IdentitySource.Windows);
    public static readonly UserIdentity Bob = new("bob@contoso.com", "Bob Jones", "bob@contoso.com", IdentitySource.EntraId);
}
