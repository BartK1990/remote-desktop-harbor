using System.Security.Cryptography;
using Harbor.Core.Storage;

namespace Harbor.Core.Security;

/// <summary>Loads or creates the data-encryption key of a store. See ADR-0005/0006.</summary>
public static class VaultKeyManager
{
    /// <summary>
    /// Returns a cipher for the store's primary key, creating and persisting a new key (wrapped with
    /// <paramref name="protectorForNewKey"/>) if the store has none yet. Safe against two clients racing to create it.
    /// </summary>
    public static async Task<SecretCipher> OpenOrCreateAsync(
        IConnectionStore store,
        IKeyProtector protectorForNewKey,
        IKeyProtectorResolver resolver,
        CancellationToken cancellationToken = default)
    {
        var existing = await store.GetVaultKeyAsync(VaultKeyRecord.PrimarySlot, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Unwrap(existing, resolver);
        }

        var key = SecretCipher.GenerateKey();
        try
        {
            var record = new VaultKeyRecord(VaultKeyRecord.PrimarySlot, Guid.NewGuid(), protectorForNewKey.Scheme,
                protectorForNewKey.Protect(key), DateTimeOffset.UtcNow);
            if (await store.TryAddVaultKeyAsync(record, cancellationToken).ConfigureAwait(false))
            {
                return new SecretCipher(record.KeyId, key);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }

        // Another client created the key first; use theirs.
        var winner = await store.GetVaultKeyAsync(VaultKeyRecord.PrimarySlot, cancellationToken).ConfigureAwait(false)
            ?? throw new VaultKeyException("Vault key could not be created or read.");
        return Unwrap(winner, resolver);
    }

    /// <summary>Opens the key of a store without creating one. Returns null when the store has no key.</summary>
    public static async Task<SecretCipher?> TryOpenAsync(
        IConnectionStore store, IKeyProtectorResolver resolver, CancellationToken cancellationToken = default)
    {
        var existing = await store.GetVaultKeyAsync(VaultKeyRecord.PrimarySlot, cancellationToken).ConfigureAwait(false);
        return existing is null ? null : Unwrap(existing, resolver);
    }

    /// <summary>
    /// Stores <paramref name="cipher"/>'s key in <paramref name="store"/>, wrapped with <paramref name="protector"/>
    /// (this user/machine for a cache; the shared scheme when re-protecting a shared vault).
    /// </summary>
    public static Task CacheAsync(
        IConnectionStore store, SecretCipher cipher, IKeyProtector protector, CancellationToken cancellationToken = default)
    {
        var key = cipher.ExportKey();
        try
        {
            var record = new VaultKeyRecord(VaultKeyRecord.PrimarySlot, cipher.KeyId, protector.Scheme,
                protector.Protect(key), DateTimeOffset.UtcNow);
            return store.ReplaceVaultKeyAsync(record, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static SecretCipher Unwrap(VaultKeyRecord record, IKeyProtectorResolver resolver)
    {
        var key = resolver.Resolve(record.Scheme).Unprotect(record.ProtectedKey);
        try
        {
            return new SecretCipher(record.KeyId, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
