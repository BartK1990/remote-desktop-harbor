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
