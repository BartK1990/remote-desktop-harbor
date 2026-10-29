using Harbor.Core.Model;
using Harbor.Core.Security;

namespace Harbor.Core.Storage;

/// <summary>
/// Persistence for connection entries and the store's wrapped vault key. Implemented once over
/// ADO.NET for every SQL provider (see Harbor.Data and ADR-0004).
/// </summary>
public interface IConnectionStore
{
    /// <summary>Creates or upgrades the schema.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConnectionEntry>> ListAsync(bool includeDeleted = false, CancellationToken cancellationToken = default);

    /// <summary>Entries (including tombstones) whose <see cref="ConnectionEntry.UpdatedUtc"/> is after <paramref name="since"/>.</summary>
    Task<IReadOnlyList<ConnectionEntry>> ListChangedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default);

    Task<ConnectionEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts (when <see cref="ConnectionEntry.Version"/> is 0) or updates the entry, using the version as an
    /// optimistic concurrency token. Timestamps and version are assigned by the store. Returns the stored entry.
    /// </summary>
    /// <exception cref="ConcurrencyConflictException">The entry was changed or deleted by someone else.</exception>
    Task<ConnectionEntry> SaveAsync(ConnectionEntry entry, string modifiedBy, CancellationToken cancellationToken = default);

    /// <summary>Marks the entry deleted (tombstone, so caches learn about it) and drops its secret.</summary>
    /// <exception cref="ConcurrencyConflictException">The entry was changed by someone else.</exception>
    Task DeleteAsync(Guid id, long expectedVersion, string deletedBy, CancellationToken cancellationToken = default);

    /// <summary>Writes entries verbatim (version and timestamps included). Used to fill the offline cache.</summary>
    Task ApplyReplicaAsync(IEnumerable<ConnectionEntry> entries, CancellationToken cancellationToken = default);

    Task<VaultKeyRecord?> GetVaultKeyAsync(string slot, CancellationToken cancellationToken = default);

    /// <summary>Adds the key if the slot is free. Returns false if another writer already added one.</summary>
    Task<bool> TryAddVaultKeyAsync(VaultKeyRecord record, CancellationToken cancellationToken = default);

    Task ReplaceVaultKeyAsync(VaultKeyRecord record, CancellationToken cancellationToken = default);

    Task<string?> GetMetaAsync(string key, CancellationToken cancellationToken = default);

    Task SetMetaAsync(string key, string value, CancellationToken cancellationToken = default);
}

public class ConcurrencyConflictException(string message) : Exception(message);
