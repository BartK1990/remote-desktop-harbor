using Harbor.Core.Model;
using Harbor.Core.Security;
using Harbor.Core.Services;
using Harbor.Core.Storage;

namespace Harbor.Data.Tests;

// SPEC FR-29, ADR-0038: entries kept on this PC only while using a shared database, and moving them both ways.
public class LocalOnlyEntryTests
{
    private static readonly TestKeyProtector AliceLocal = new("local-alice", "alice-machine");
    private static readonly TestKeyProtector BobLocal = new("local-bob", "bob-machine");
    private static readonly TestKeyProtector Shared = new("shared-group", "group-secret");
    private static readonly KeyProtectorResolver Resolver = new([AliceLocal, BobLocal, Shared]);

    private sealed class Setup : IDisposable
    {
        public TempDb RemoteDb { get; } = new();
        public TempDb Cache { get; } = new();
        public TempDb Vault { get; } = new();
        public SwitchableStore Remote { get; }

        public Setup() => Remote = new SwitchableStore(RemoteDb.Store);

        public HarborWorkspace Alice() =>
            HarborWorkspace.CreateRemote(Users.Alice, Cache.Store, Remote, AliceLocal, Shared, Resolver, true, Vault.Store);

        public void Dispose()
        {
            RemoteDb.Dispose();
            Cache.Dispose();
            Vault.Dispose();
        }
    }

    [Fact]
    public async Task Local_only_entries_never_reach_the_shared_database()
    {
        using var s = new Setup();
        using var ws = s.Alice();
        await ws.OpenAsync();
        Assert.True(ws.SupportsLocalEntries);

        var saved = await ws.SaveAsync(ConnectionEntry.CreateNew("mine") with { Host = "lab", IsLocalOnly = true }, SecretUpdate.Set("pw"));
        await ws.SaveAsync(ConnectionEntry.CreateNew("ours") with { Host = "srv" }, SecretUpdate.Set("team"));

        Assert.True(saved.IsLocalOnly);
        Assert.Equal("pw", ws.RevealPassword(saved));
        Assert.Equal(["ours"], (await s.RemoteDb.Store.ListAsync(includeDeleted: true)).Select(e => e.Name));
        Assert.Equal(["mine"], (await s.Vault.Store.ListAsync()).Select(e => e.Name));

        var all = await ws.ListAsync();
        Assert.Equal(2, all.Count);
        Assert.True(all.Single(e => e.Name == "mine").IsLocalOnly);
        Assert.False(all.Single(e => e.Name == "ours").IsLocalOnly);

        // The local vault has its own key, wrapped for this user and machine.
        Assert.Equal("local-alice", (await s.Vault.Store.GetVaultKeyAsync(VaultKeyRecord.PrimarySlot))!.Scheme);
    }

    [Fact]
    public async Task Local_only_entries_can_be_edited_and_deleted_while_offline()
    {
        using var s = new Setup();
        using (var first = s.Alice())
        {
            await first.OpenAsync();
            await first.SaveAsync(ConnectionEntry.CreateNew("ours") with { Host = "srv" }, SecretUpdate.Keep);
        }

        s.Remote.Offline = true;
        using var ws = s.Alice();
        await ws.OpenAsync();
        Assert.False(ws.CanWrite);

        var mine = await ws.SaveAsync(ConnectionEntry.CreateNew("mine") with { IsLocalOnly = true }, SecretUpdate.Set("pw"));
        Assert.True(ws.CanWriteEntry(mine));
        Assert.False(ws.CanWriteEntry((await ws.ListAsync()).Single(e => e.Name == "ours")));

        var renamed = await ws.SaveAsync(mine with { Name = "mine2" }, SecretUpdate.Keep);
        Assert.Equal("pw", ws.RevealPassword(renamed));
        await ws.DeleteAsync(renamed);
        Assert.Equal(["ours"], (await ws.ListAsync()).Select(e => e.Name));

        var ours = Assert.Single(await ws.ListAsync());
        await Assert.ThrowsAsync<WorkspaceOfflineException>(() => ws.MoveAsync(ours, toLocal: true));
    }

    [Fact]
    public async Task Entries_move_between_the_shared_database_and_this_pc_with_their_passwords()
    {
        using var s = new Setup();
        using var ws = s.Alice();
        await ws.OpenAsync();

        var account = new ConnectionAccount { Id = Guid.NewGuid(), UserName = "admin" };
        var shared = await ws.SaveAsync(ConnectionEntry.CreateNew("srv") with { Host = "srv", Accounts = [account] },
            SecretUpdate.Set("pw"), new Dictionary<Guid, SecretUpdate> { [account.Id] = SecretUpdate.Set("admin-pw") });

        var local = await ws.MoveAsync(shared, toLocal: true);
        Assert.True(local.IsLocalOnly);
        Assert.NotEqual(shared.Id, local.Id);
        Assert.Equal("pw", ws.RevealPassword(local));
        Assert.Equal("admin-pw", ws.RevealPassword(local, local.Accounts.Single().Id));

        // Gone from the shared database (tombstone without secrets, so other caches drop it), present on this PC only.
        var tombstone = Assert.Single(await s.RemoteDb.Store.ListAsync(includeDeleted: true));
        Assert.True(tombstone.IsDeleted);
        Assert.Null(tombstone.PasswordCipher);
        Assert.Equal([local.Id], (await ws.ListAsync()).Select(e => e.Id));

        var back = await ws.MoveAsync(local, toLocal: false);
        Assert.False(back.IsLocalOnly);
        Assert.Equal("pw", ws.RevealPassword(back));
        Assert.Equal("admin-pw", ws.RevealPassword(back, back.Accounts.Single().Id));
        Assert.Empty(await s.Vault.Store.ListAsync());
        Assert.Equal([back.Id], (await ws.ListAsync()).Select(e => e.Id));

        // Bob, on another machine, sees the entry again with its password.
        using var bobCache = new TempDb();
        using var bob = HarborWorkspace.CreateRemote(Users.Bob, bobCache.Store, s.Remote, BobLocal, Shared, Resolver, true);
        await bob.OpenAsync();
        var seen = Assert.Single(await bob.ListAsync());
        Assert.Equal("pw", bob.RevealPassword(seen));
    }

    [Fact]
    public async Task A_stale_entry_is_not_moved()
    {
        using var s = new Setup();
        using var ws = s.Alice();
        await ws.OpenAsync();
        var shared = await ws.SaveAsync(ConnectionEntry.CreateNew("srv"), SecretUpdate.Set("pw"));
        await ws.SaveAsync(shared with { Name = "srv2" }, SecretUpdate.Keep); // someone changed it meanwhile

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => ws.MoveAsync(shared, toLocal: true));
        Assert.Empty(await s.Vault.Store.ListAsync());
        Assert.Equal(["srv2"], (await ws.ListAsync()).Select(e => e.Name));
    }

    [Fact]
    public async Task A_local_vault_that_cannot_be_opened_only_disables_local_entries()
    {
        using var s = new Setup();
        using (var bobVault = HarborWorkspace.CreateLocal(Users.Bob, s.Vault.Store, BobLocal, new KeyProtectorResolver([BobLocal])))
        {
            await bobVault.OpenAsync(); // the vault belongs to Bob's machine
        }

        using var ws = HarborWorkspace.CreateRemote(Users.Alice, s.Cache.Store, s.Remote, AliceLocal, Shared,
            new KeyProtectorResolver([AliceLocal, Shared]), true, s.Vault.Store);
        await ws.OpenAsync();

        Assert.True(ws.IsOnline);
        Assert.False(ws.SupportsLocalEntries);
        Assert.NotNull(ws.LocalVaultError);
    }

    // SPEC FR-69, ADR-0048
    [Fact]
    public async Task Delete_many_removes_local_and_shared_entries_and_reports_the_ones_it_could_not_delete()
    {
        using var s = new Setup();
        using var ws = s.Alice();
        await ws.OpenAsync();
        for (var i = 0; i < 3; i++)
        {
            await ws.SaveAsync(ConnectionEntry.CreateNew($"lab{i}") with { Host = "lab", IsLocalOnly = true }, SecretUpdate.Set("pw"));
        }

        await ws.SaveAsync(ConnectionEntry.CreateNew("ours") with { Host = "srv" }, SecretUpdate.Set("team"));
        var stale = await ws.SaveAsync(ConnectionEntry.CreateNew("stale") with { Host = "old" }, SecretUpdate.Keep);
        await ws.SaveAsync(stale with { Notes = "changed by someone else" }, SecretUpdate.Keep); // `stale` is now an old version

        var all = await ws.ListAsync();
        var result = await ws.DeleteManyAsync([.. all.Where(e => e.Name != "stale"), stale]);

        Assert.Equal(["lab0", "lab1", "lab2", "ours"], result.Deleted.Select(e => e.Name).Order());
        var failed = Assert.Single(result.Failed);
        Assert.Equal("stale", failed.Entry.Name);
        Assert.IsType<ConcurrencyConflictException>(failed.Error);
        Assert.Equal(["stale"], (await ws.ListAsync()).Select(e => e.Name));
        Assert.Empty(await s.Vault.Store.ListAsync());
    }

    // SPEC FR-69: offline, entries kept on this PC are still deleted; shared ones are kept and reported.
    [Fact]
    public async Task Delete_many_while_offline_deletes_only_entries_kept_on_this_pc()
    {
        using var s = new Setup();
        using var ws = s.Alice();
        await ws.OpenAsync();
        await ws.SaveAsync(ConnectionEntry.CreateNew("mine") with { Host = "lab", IsLocalOnly = true }, SecretUpdate.Keep);
        await ws.SaveAsync(ConnectionEntry.CreateNew("ours") with { Host = "srv" }, SecretUpdate.Keep);
        var all = await ws.ListAsync();

        s.Remote.Offline = true;
        await ws.SyncAsync();
        Assert.False(ws.IsOnline);
        var result = await ws.DeleteManyAsync(all);

        Assert.Equal("mine", Assert.Single(result.Deleted).Name);
        var failed = Assert.Single(result.Failed);
        Assert.Equal("ours", failed.Entry.Name);
        Assert.IsType<WorkspaceOfflineException>(failed.Error);
        Assert.Equal(["ours"], (await ws.ListAsync()).Select(e => e.Name));
    }
}
