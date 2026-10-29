using System.Data.Common;
using Harbor.Core.Model;
using Harbor.Core.Security;
using Harbor.Core.Services;
using Harbor.Data.Providers;

namespace Harbor.Data.Tests;

// SPEC FR-09, ADR-0019: several user accounts per entry.
public class AccountTests
{
    private static readonly TestKeyProtector Local = new("local-alice", "alice-machine");
    private static readonly TestKeyProtector Shared = new("shared-group", "group-secret");
    private static readonly KeyProtectorResolver Resolver = new([Local, new TestKeyProtector("local-bob", "bob-machine"), Shared]);

    private static ConnectionEntry WithAccounts(params string[] users) => ConnectionEntry.CreateNew("srv") with
    {
        Host = "srv", Domain = "CONTOSO", UserName = "admin",
        Accounts = users.Select(u => ConnectionAccount.CreateNew(u) with { PasswordCipher = [7] }).ToList(),
    };

    [Fact]
    public async Task Accounts_are_stored_in_order_and_replaced_on_update()
    {
        using var db = new TempDb();
        await db.Store.InitializeAsync();

        var stored = await db.Store.SaveAsync(WithAccounts("svc", "ops"), "alice");
        Assert.Equal(["svc", "ops"], stored.Accounts.Select(a => a.UserName));
        Assert.Equal(new byte[] { 7 }, stored.Accounts[0].PasswordCipher);

        var reordered = await db.Store.SaveAsync(stored with { Accounts = [stored.Accounts[1] with { Domain = "LAB" }] }, "alice");
        var only = Assert.Single(reordered.Accounts);
        Assert.Equal((stored.Accounts[1].Id, "ops", "LAB"), (only.Id, only.UserName, only.Domain));
        Assert.Equal(2, reordered.Version);
        Assert.Equal(reordered.Accounts.Single().Id, Assert.Single(await db.Store.ListAsync()).Accounts.Single().Id);
    }

    [Fact]
    public async Task Delete_drops_the_accounts_and_their_secrets()
    {
        using var db = new TempDb();
        await db.Store.InitializeAsync();
        var stored = await db.Store.SaveAsync(WithAccounts("svc"), "alice");
        await db.Store.DeleteAsync(stored.Id, stored.Version, "alice");

        Assert.Empty(Assert.Single(await db.Store.ListAsync(includeDeleted: true)).Accounts);
        Assert.Equal(0, await CountAccountRowsAsync(db));
    }

    [Fact]
    public async Task Replica_carries_accounts_and_tombstones_remove_them()
    {
        using var source = new TempDb();
        using var replica = new TempDb();
        await source.Store.InitializeAsync();
        await replica.Store.InitializeAsync();

        var stored = await source.Store.SaveAsync(WithAccounts("svc", "ops"), "alice");
        await replica.Store.ApplyReplicaAsync(await source.Store.ListChangedSinceAsync(DateTimeOffset.UnixEpoch));
        await replica.Store.ApplyReplicaAsync(await source.Store.ListChangedSinceAsync(DateTimeOffset.UnixEpoch)); // idempotent
        Assert.Equal(["svc", "ops"], Assert.Single(await replica.Store.ListAsync()).Accounts.Select(a => a.UserName));

        await source.Store.DeleteAsync(stored.Id, stored.Version, "alice");
        await replica.Store.ApplyReplicaAsync(await source.Store.ListChangedSinceAsync(DateTimeOffset.UnixEpoch));
        Assert.Empty(await replica.Store.ListAsync());
        Assert.Equal(0, await CountAccountRowsAsync(replica));
    }

    [Fact]
    public async Task Existing_database_is_upgraded_and_keeps_its_entries()
    {
        using var db = new TempDb();
        var v1 = new SqlHarborStore(new FirstMigrationOnly(DatabaseProviders.Sqlite), SqliteProvider.ConnectionStringForFile(db.Path));
        await v1.InitializeAsync();
        var old = await SaveV1EntryAsync(db.Path);

        await db.Store.InitializeAsync();

        var entry = Assert.Single(await db.Store.ListAsync());
        Assert.Equal((old, "admin"), (entry.Id, entry.UserName));
        Assert.Empty(entry.Accounts);
        await db.Store.SaveAsync(entry with { Accounts = [ConnectionAccount.CreateNew("svc")] }, "alice");
        Assert.Single(Assert.Single(await db.Store.ListAsync()).Accounts);
    }

    [Fact]
    public async Task Each_account_password_is_encrypted_separately_and_bound_to_its_account()
    {
        using var db = new TempDb();
        using var ws = HarborWorkspace.CreateLocal(Users.Alice, db.Store, Local, Resolver);
        await ws.OpenAsync();
        var svc = ConnectionAccount.CreateNew("svc");
        var ops = ConnectionAccount.CreateNew("ops");

        var saved = await ws.SaveAsync(ConnectionEntry.CreateNew("srv") with { Host = "srv", UserName = "admin", Accounts = [svc, ops] },
            SecretUpdate.Set("default-pw"), new Dictionary<Guid, SecretUpdate> { [svc.Id] = SecretUpdate.Set("svc-pw") });

        Assert.Equal("default-pw", ws.RevealPassword(saved));
        Assert.Equal("svc-pw", ws.RevealPassword(saved, svc.Id));
        Assert.Null(ws.RevealPassword(saved, ops.Id));
        Assert.Equal(-1, saved.Accounts[0].PasswordCipher!.AsSpan().IndexOf("svc-pw"u8));

        // A cipher copied onto another account (or the entry) does not decrypt.
        var swapped = saved with { Accounts = [saved.Accounts[0], saved.Accounts[1] with { PasswordCipher = saved.Accounts[0].PasswordCipher }] };
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => ws.RevealPassword(swapped, ops.Id));
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() =>
            ws.RevealPassword(saved with { PasswordCipher = saved.Accounts[0].PasswordCipher }));

        // Missing updates keep a password; Clear removes it.
        var edited = await ws.SaveAsync(saved with { Name = "srv2" }, SecretUpdate.Keep,
            new Dictionary<Guid, SecretUpdate> { [ops.Id] = SecretUpdate.Set("ops-pw") });
        Assert.Equal(("default-pw", "svc-pw", "ops-pw"),
            (ws.RevealPassword(edited), ws.RevealPassword(edited, svc.Id), ws.RevealPassword(edited, ops.Id)));
        var cleared = await ws.SaveAsync(edited, SecretUpdate.Keep, new Dictionary<Guid, SecretUpdate> { [svc.Id] = SecretUpdate.Clear });
        Assert.Null(ws.RevealPassword(cleared, svc.Id));
        Assert.Throws<InvalidOperationException>(() => ws.RevealPassword(cleared, Guid.NewGuid()));
    }

    [Fact]
    public async Task Accounts_are_shared_through_the_remote_database()
    {
        using var remoteDb = new TempDb();
        using var aliceCache = new TempDb();
        using var bobCache = new TempDb();
        var svc = ConnectionAccount.CreateNew("svc");

        using var alice = HarborWorkspace.CreateRemote(Users.Alice, aliceCache.Store, remoteDb.Store, Local, Shared, Resolver, true);
        await alice.OpenAsync();
        await alice.SaveAsync(ConnectionEntry.CreateNew("srv") with { Host = "srv", Accounts = [svc] }, SecretUpdate.Keep,
            new Dictionary<Guid, SecretUpdate> { [svc.Id] = SecretUpdate.Set("svc-pw") });

        using var bob = HarborWorkspace.CreateRemote(Users.Bob, bobCache.Store, remoteDb.Store,
            new TestKeyProtector("local-bob", "bob-machine"), Shared, Resolver, true);
        await bob.OpenAsync();
        var seen = Assert.Single(await bob.ListAsync());
        Assert.Equal("svc-pw", bob.RevealPassword(seen, Assert.Single(seen.Accounts).Id));
    }

    [Fact]
    public async Task Presence_records_the_account_in_use()
    {
        using var db = new TempDb();
        await db.Store.InitializeAsync();
        var connectionId = Guid.NewGuid();
        await db.Store.StartSessionAsync(Guid.NewGuid(), connectionId, "CONTOSO\\svc", Users.Alice, "PC-01", TimeSpan.FromMinutes(2));

        var session = Assert.Single(await db.Store.ListActiveAsync());
        Assert.Equal("CONTOSO\\svc", session.AccountName);
        Assert.True(session.UsesAccount("contoso\\SVC"));
        Assert.False(session.UsesAccount("CONTOSO\\admin"));
    }

    private static async Task<long> CountAccountRowsAsync(TempDb db)
    {
        await using var connection = DatabaseProviders.Sqlite.CreateConnection(SqliteProvider.ConnectionStringForFile(db.Path));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ConnectionAccounts";
        return (long)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Writes an entry the way a client from before ADR-0019 did (schema version 1).</summary>
    private static async Task<Guid> SaveV1EntryAsync(string path)
    {
        var id = Guid.NewGuid();
        await using var connection = DatabaseProviders.Sqlite.CreateConnection(SqliteProvider.ConnectionStringForFile(path));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO Connections (Id, Name, Kind, Host, Port, UserName, Version, CreatedUtcMs, UpdatedUtcMs, IsDeleted)
            VALUES ('{id:D}', 'old', 0, 'srv', 3389, 'admin', 1, 1, 1, 0)
            """;
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>The provider as it was at schema version 1.</summary>
    private sealed class FirstMigrationOnly(IDatabaseProvider inner) : IDatabaseProvider
    {
        public string Name => inner.Name;
        public DbConnection CreateConnection(string connectionString) => inner.CreateConnection(connectionString);
        public string NowUnixMs => inner.NowUnixMs;
        public string? PrepareSql => inner.PrepareSql;
        public string EnsureVersionTableSql => inner.EnsureVersionTableSql;
        public IReadOnlyList<string> Migrations => [inner.Migrations[0]];
        public bool IsUniqueViolation(DbException exception) => inner.IsUniqueViolation(exception);
    }
}
