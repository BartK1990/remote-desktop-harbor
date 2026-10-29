using Harbor.Core.Model;
using Harbor.Core.Storage;

namespace Harbor.Data.Tests;

// SPEC FR-01, FR-02, FR-20, FR-23, FR-24, NFR-20
public class SqlHarborStoreTests
{
    [Fact]
    public async Task Initialize_is_idempotent()
    {
        using var db = new TempDb();
        await db.Store.InitializeAsync();
        await db.Store.InitializeAsync();
        Assert.Empty(await db.Store.ListAsync());
    }

    [Fact]
    public async Task Insert_update_and_read_back()
    {
        using var db = new TempDb();
        await db.Store.InitializeAsync();
        var entry = ConnectionEntry.CreateNew("Prod DC") with
        {
            Folder = "Prod", Host = "dc01", Port = 3390, Domain = "CONTOSO", UserName = "admin", PasswordCipher = [1, 2, 3],
            RdpSettings = "audiomode:i:2", Notes = "n",
        };

        var stored = await db.Store.SaveAsync(entry, "alice");
        Assert.Equal(1, stored.Version);
        Assert.Equal("alice", stored.UpdatedBy);
        Assert.True(stored.CreatedUtc > DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal(new byte[] { 1, 2, 3 }, stored.PasswordCipher);
        Assert.Equal(3390, stored.Port);
        Assert.Equal(EntryKind.RdpConnection, stored.Kind);

        var updated = await db.Store.SaveAsync(stored with { Name = "Prod DC 1", Kind = EntryKind.CredentialOnly }, "bob");
        Assert.Equal(2, updated.Version);
        Assert.Equal("Prod DC 1", updated.Name);
        Assert.Equal(EntryKind.CredentialOnly, updated.Kind);
        Assert.Equal("bob", updated.UpdatedBy);
    }

    [Fact]
    public async Task Stale_update_is_rejected()
    {
        using var db = new TempDb();
        await db.Store.InitializeAsync();
        var v1 = await db.Store.SaveAsync(ConnectionEntry.CreateNew("a"), "alice");
        await db.Store.SaveAsync(v1 with { Name = "b" }, "alice");
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => db.Store.SaveAsync(v1 with { Name = "c" }, "bob"));
    }

    [Fact]
    public async Task Delete_leaves_tombstone_without_secret()
    {
        using var db = new TempDb();
        await db.Store.InitializeAsync();
        var e = await db.Store.SaveAsync(ConnectionEntry.CreateNew("a") with { PasswordCipher = [9] }, "alice");
        await db.Store.DeleteAsync(e.Id, e.Version, "alice");

        Assert.Empty(await db.Store.ListAsync());
        var tomb = Assert.Single(await db.Store.ListAsync(includeDeleted: true));
        Assert.True(tomb.IsDeleted);
        Assert.Null(tomb.PasswordCipher);
        Assert.Equal(2, tomb.Version);
    }

    [Fact]
    public async Task Changed_since_and_replica_roundtrip()
    {
        using var source = new TempDb();
        using var replica = new TempDb();
        await source.Store.InitializeAsync();
        await replica.Store.InitializeAsync();

        var a = await source.Store.SaveAsync(ConnectionEntry.CreateNew("a"), "alice");
        var changes = await source.Store.ListChangedSinceAsync(DateTimeOffset.UnixEpoch);
        await replica.Store.ApplyReplicaAsync(changes);
        await replica.Store.ApplyReplicaAsync(changes); // idempotent

        var copy = Assert.Single(await replica.Store.ListAsync());
        Assert.Equal(a.Id, copy.Id);
        Assert.Equal(a.Version, copy.Version);
        Assert.Equal(a.UpdatedUtc, copy.UpdatedUtc);

        Assert.Empty(await source.Store.ListChangedSinceAsync(a.UpdatedUtc));
    }

    [Fact]
    public async Task Meta_upsert()
    {
        using var db = new TempDb();
        await db.Store.InitializeAsync();
        Assert.Null(await db.Store.GetMetaAsync("k"));
        await db.Store.SetMetaAsync("k", "1");
        await db.Store.SetMetaAsync("k", "2");
        Assert.Equal("2", await db.Store.GetMetaAsync("k"));
    }
}
