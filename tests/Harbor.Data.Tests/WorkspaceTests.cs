using Harbor.Core.Model;
using Harbor.Core.Security;
using Harbor.Core.Services;
using Harbor.Core.Storage;

namespace Harbor.Data.Tests;

// SPEC FR-04, FR-20..FR-24, NFR-10, NFR-13
public class WorkspaceTests
{
    private static readonly TestKeyProtector AliceLocal = new("local-alice", "alice-machine");
    private static readonly TestKeyProtector BobLocal = new("local-bob", "bob-machine");
    private static readonly TestKeyProtector Shared = new("shared-group", "group-secret");
    private static readonly KeyProtectorResolver Resolver = new([AliceLocal, BobLocal, Shared]);

    [Fact]
    public async Task Local_mode_encrypts_passwords_at_rest()
    {
        using var db = new TempDb();
        using var ws = HarborWorkspace.CreateLocal(Users.Alice, db.Store, AliceLocal, Resolver);
        await ws.OpenAsync();

        var saved = await ws.SaveAsync(ConnectionEntry.CreateNew("srv") with { Host = "srv" }, SecretUpdate.Set("hunter2"));
        Assert.NotNull(saved.PasswordCipher);
        Assert.Equal(-1, saved.PasswordCipher!.AsSpan().IndexOf("hunter2"u8));
        Assert.Equal("hunter2", ws.RevealPassword(saved));

        var key = await db.Store.GetVaultKeyAsync(VaultKeyRecord.PrimarySlot);
        Assert.Equal("local-alice", key!.Scheme);

        var kept = await ws.SaveAsync(saved with { Name = "srv2" }, SecretUpdate.Keep);
        Assert.Equal("hunter2", ws.RevealPassword(kept));
        var cleared = await ws.SaveAsync(kept, SecretUpdate.Clear);
        Assert.Null(ws.RevealPassword(cleared));
    }

    [Fact]
    public async Task Local_vault_reopens_with_same_key()
    {
        using var db = new TempDb();
        ConnectionEntry saved;
        using (var ws = HarborWorkspace.CreateLocal(Users.Alice, db.Store, AliceLocal, Resolver))
        {
            await ws.OpenAsync();
            saved = await ws.SaveAsync(ConnectionEntry.CreateNew("a"), SecretUpdate.Set("pw"));
        }

        using var again = HarborWorkspace.CreateLocal(Users.Alice, db.Store, AliceLocal, Resolver);
        await again.OpenAsync();
        Assert.Equal("pw", again.RevealPassword(Assert.Single(await again.ListAsync())));
        _ = saved;
    }

    [Fact]
    public async Task Remote_mode_shares_entries_between_users_and_caches_them()
    {
        using var remoteDb = new TempDb();
        using var aliceCache = new TempDb();
        using var bobCache = new TempDb();

        using var alice = HarborWorkspace.CreateRemote(Users.Alice, aliceCache.Store, remoteDb.Store, AliceLocal, Shared, Resolver, true);
        await alice.OpenAsync();
        Assert.True(alice.IsOnline);
        await alice.SaveAsync(ConnectionEntry.CreateNew("shared srv") with { Host = "srv" }, SecretUpdate.Set("s3cret"));

        using var bob = HarborWorkspace.CreateRemote(Users.Bob, bobCache.Store, remoteDb.Store, BobLocal, Shared, Resolver, true);
        await bob.OpenAsync();
        var seen = Assert.Single(await bob.ListAsync());
        Assert.Equal("s3cret", bob.RevealPassword(seen));
        Assert.Equal("CONTOSO\\alice", seen.UpdatedBy);

        // Each cache holds the shared key wrapped for its own user/machine.
        Assert.Equal("local-alice", (await aliceCache.Store.GetVaultKeyAsync(VaultKeyRecord.PrimarySlot))!.Scheme);
        Assert.Equal("local-bob", (await bobCache.Store.GetVaultKeyAsync(VaultKeyRecord.PrimarySlot))!.Scheme);
        Assert.Equal("shared-group", (await remoteDb.Store.GetVaultKeyAsync(VaultKeyRecord.PrimarySlot))!.Scheme);
    }

    [Fact]
    public async Task Remote_mode_works_read_only_from_cache_when_offline()
    {
        using var remoteDb = new TempDb();
        using var cache = new TempDb();
        var remote = new SwitchableStore(remoteDb.Store);

        using (var first = HarborWorkspace.CreateRemote(Users.Alice, cache.Store, remote, AliceLocal, Shared, Resolver, true))
        {
            await first.OpenAsync();
            await first.SaveAsync(ConnectionEntry.CreateNew("a") with { Host = "a" }, SecretUpdate.Set("pw"));
        }

        remote.Offline = true;
        using var ws = HarborWorkspace.CreateRemote(Users.Alice, cache.Store, remote, AliceLocal, Shared, Resolver, true);
        await ws.OpenAsync();

        Assert.False(ws.IsOnline);
        Assert.False(ws.CanWrite);
        var entry = Assert.Single(await ws.ListAsync());
        Assert.Equal("pw", ws.RevealPassword(entry));
        await Assert.ThrowsAsync<WorkspaceOfflineException>(() => ws.SaveAsync(entry with { Name = "b" }, SecretUpdate.Keep));

        remote.Offline = false;
        await ws.SyncAsync();
        Assert.True(ws.IsOnline);
        await ws.SaveAsync(entry with { Name = "b" }, SecretUpdate.Keep);
        Assert.Equal("b", Assert.Single(await ws.ListAsync()).Name);
    }

    [Fact]
    public async Task Shared_key_is_unwrapped_once_per_machine_then_taken_from_the_cache()
    {
        // ADR-0006: after the first unwrap no passphrase prompt (or DC) is needed on later starts.
        using var remoteDb = new TempDb();
        using var aliceCache = new TempDb();
        using var bobCache = new TempDb();
        var shared = new CountingKeyProtector(Shared);
        var resolver = new KeyProtectorResolver([AliceLocal, BobLocal, shared]);

        using (var alice = HarborWorkspace.CreateRemote(Users.Alice, aliceCache.Store, remoteDb.Store, AliceLocal, shared, resolver, true))
        {
            await alice.OpenAsync();
            await alice.SaveAsync(ConnectionEntry.CreateNew("a"), SecretUpdate.Set("pw"));
        }

        for (var start = 0; start < 3; start++)
        {
            using var bob = HarborWorkspace.CreateRemote(Users.Bob, bobCache.Store, remoteDb.Store, BobLocal, shared, resolver, true);
            await bob.OpenAsync();
            Assert.True(bob.IsOnline);
            Assert.Equal("pw", bob.RevealPassword(Assert.Single(await bob.ListAsync())));
        }

        Assert.Equal(1, shared.UnprotectCount);
    }

    // SPEC FR-28, ADR-0016
    [Fact]
    public async Task Sign_in_protected_vault_opens_for_signed_in_users_without_a_passphrase()
    {
        using var remoteDb = new TempDb();
        using var aliceCache = new TempDb();
        using var bobCache = new TempDb();
        var passphrase = new CountingKeyProtector(Shared);
        var resolver = new KeyProtectorResolver([AliceLocal, BobLocal, passphrase, new SignInKeyProtector(Users.Bob)]);

        using (var alice = HarborWorkspace.CreateRemote(Users.Alice, aliceCache.Store, remoteDb.Store, AliceLocal,
                   new SignInKeyProtector(Users.Alice), resolver, true))
        {
            await alice.OpenAsync();
            await alice.SaveAsync(ConnectionEntry.CreateNew("a"), SecretUpdate.Set("pw"));
        }

        using var bob = HarborWorkspace.CreateRemote(Users.Bob, bobCache.Store, remoteDb.Store, BobLocal, passphrase, resolver, true);
        await bob.OpenAsync();

        Assert.True(bob.IsOnline);
        Assert.Equal("pw", bob.RevealPassword(Assert.Single(await bob.ListAsync())));
        Assert.Equal(SignInKeyProtector.SchemeName, (await remoteDb.Store.GetVaultKeyAsync(VaultKeyRecord.PrimarySlot))!.Scheme);
        Assert.Equal(0, passphrase.UnprotectCount);
    }

    [Fact]
    public async Task Sign_in_protected_vault_does_not_open_without_sign_in()
    {
        using var remoteDb = new TempDb();
        using var aliceCache = new TempDb();
        using var carolCache = new TempDb();
        using (var alice = HarborWorkspace.CreateRemote(Users.Alice, aliceCache.Store, remoteDb.Store, AliceLocal,
                   new SignInKeyProtector(Users.Alice), Resolver, true))
        {
            await alice.OpenAsync();
        }

        var carolResolver = new KeyProtectorResolver([BobLocal, new SignInKeyProtector(Users.Carol)]);
        using var carol = HarborWorkspace.CreateRemote(Users.Carol, carolCache.Store, remoteDb.Store, BobLocal,
            new SignInKeyProtector(Users.Carol), carolResolver, true);

        var ex = await Assert.ThrowsAsync<WorkspaceOfflineException>(() => carol.OpenAsync());
        Assert.IsType<VaultKeyException>(ex.InnerException);
    }

    [Fact]
    public async Task Existing_passphrase_vault_is_converted_once_and_keeps_its_key()
    {
        using var remoteDb = new TempDb();
        using var aliceCache = new TempDb();
        using var bobCache = new TempDb();
        var passphrase = new CountingKeyProtector(Shared);
        var resolver = new KeyProtectorResolver([AliceLocal, BobLocal, passphrase, new SignInKeyProtector(Users.Alice)]);

        using (var alice = HarborWorkspace.CreateRemote(Users.Alice, aliceCache.Store, remoteDb.Store, AliceLocal, passphrase, resolver, true))
        {
            await alice.OpenAsync();
            await alice.SaveAsync(ConnectionEntry.CreateNew("a"), SecretUpdate.Set("pw"));
        }

        var before = (await remoteDb.Store.GetVaultKeyAsync(VaultKeyRecord.PrimarySlot))!;
        Assert.Equal(Shared.Scheme, before.Scheme);

        // Alice converts: her cached key is used, so not even she needs the passphrase.
        using (var alice = HarborWorkspace.CreateRemote(Users.Alice, aliceCache.Store, remoteDb.Store, AliceLocal,
                   new SignInKeyProtector(Users.Alice), resolver, true))
        {
            alice.ConvertRemoteKeyProtection = true;
            await alice.OpenAsync();
            Assert.True(alice.RemoteKeyConverted);
        }

        var after = (await remoteDb.Store.GetVaultKeyAsync(VaultKeyRecord.PrimarySlot))!;
        Assert.Equal(SignInKeyProtector.SchemeName, after.Scheme);
        Assert.Equal(before.KeyId, after.KeyId);

        // Bob has never opened the vault and is never asked for the passphrase.
        using var bob = HarborWorkspace.CreateRemote(Users.Bob, bobCache.Store, remoteDb.Store, BobLocal, passphrase, resolver, true);
        await bob.OpenAsync();
        Assert.Equal("pw", bob.RevealPassword(Assert.Single(await bob.ListAsync())));
        Assert.Equal(0, passphrase.UnprotectCount);
    }

    [Fact]
    public async Task Without_the_convert_request_an_existing_vault_keeps_its_protection()
    {
        using var remoteDb = new TempDb();
        using var aliceCache = new TempDb();
        using (var alice = HarborWorkspace.CreateRemote(Users.Alice, aliceCache.Store, remoteDb.Store, AliceLocal, Shared, Resolver, true))
        {
            await alice.OpenAsync();
        }

        var resolver = new KeyProtectorResolver([AliceLocal, Shared, new SignInKeyProtector(Users.Alice)]);
        using (var again = HarborWorkspace.CreateRemote(Users.Alice, aliceCache.Store, remoteDb.Store, AliceLocal,
                   new SignInKeyProtector(Users.Alice), resolver, true))
        {
            await again.OpenAsync();
            Assert.False(again.RemoteKeyConverted);
        }

        Assert.Equal(Shared.Scheme, (await remoteDb.Store.GetVaultKeyAsync(VaultKeyRecord.PrimarySlot))!.Scheme);
    }

    [Fact]
    public async Task Remote_mode_without_cache_fails_when_offline()
    {
        using var remoteDb = new TempDb();
        using var cache = new TempDb();
        var remote = new SwitchableStore(remoteDb.Store) { Offline = true };
        using var ws = HarborWorkspace.CreateRemote(Users.Alice, cache.Store, remote, AliceLocal, Shared, Resolver, true);
        await Assert.ThrowsAsync<WorkspaceOfflineException>(() => ws.OpenAsync());
    }

    [Fact]
    public async Task Sync_propagates_updates_and_deletes()
    {
        using var remoteDb = new TempDb();
        using var aliceCache = new TempDb();
        using var bobCache = new TempDb();
        using var alice = HarborWorkspace.CreateRemote(Users.Alice, aliceCache.Store, remoteDb.Store, AliceLocal, Shared, Resolver, true);
        using var bob = HarborWorkspace.CreateRemote(Users.Bob, bobCache.Store, remoteDb.Store, BobLocal, Shared, Resolver, true);
        await alice.OpenAsync();
        await bob.OpenAsync();

        var a = await alice.SaveAsync(ConnectionEntry.CreateNew("a"), SecretUpdate.Keep);
        var b = await alice.SaveAsync(ConnectionEntry.CreateNew("b"), SecretUpdate.Keep);
        await bob.SyncAsync();
        Assert.Equal(2, (await bob.ListAsync()).Count);

        await alice.SaveAsync(a with { Name = "a2" }, SecretUpdate.Keep);
        await alice.DeleteAsync(b);
        await bob.SyncAsync();
        var only = Assert.Single(await bob.ListAsync());
        Assert.Equal("a2", only.Name);

        // Bob edits a stale copy -> conflict, not a silent overwrite.
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => bob.SaveAsync(a with { Name = "stale" }, SecretUpdate.Keep));
    }
}
