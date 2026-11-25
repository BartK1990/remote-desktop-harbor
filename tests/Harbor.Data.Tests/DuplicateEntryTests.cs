using Harbor.Core.Model;
using Harbor.Core.Security;
using Harbor.Core.Services;

namespace Harbor.Data.Tests;

// SPEC FR-73: duplicating an entry copies everything, passwords included, under new ids.
public class DuplicateEntryTests
{
    private static readonly TestKeyProtector AliceLocal = new("local-alice", "alice-machine");
    private static readonly TestKeyProtector Shared = new("shared-group", "group-secret");
    private static readonly KeyProtectorResolver Resolver = new([AliceLocal, Shared]);

    [Fact]
    public async Task Duplicate_keeps_settings_accounts_and_passwords_under_new_ids()
    {
        using var db = new TempDb();
        using var ws = HarborWorkspace.CreateLocal(Users.Alice, db.Store, AliceLocal, Resolver);
        await ws.OpenAsync();
        var ops = ConnectionAccount.CreateNew("ops");
        var original = await ws.SaveAsync(ConnectionEntry.CreateNew("Web 01") with
        {
            Folder = "Production/Web", Host = "10.0.0.5", Port = 3390, Domain = "CONTOSO", UserName = "svc",
            RdpSettings = "audiomode:i:2", Notes = "front end", Accounts = [ops],
        }, SecretUpdate.Set("pw"), new Dictionary<Guid, SecretUpdate> { [ops.Id] = SecretUpdate.Set("ops-pw") });

        var copy = ws.Duplicate(original, ["Web 01"]);

        Assert.Equal("Web 01 (copy)", copy.Name);
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(0, copy.Version);
        Assert.Equal((original.Folder, original.Host, original.Port, original.Domain, original.UserName, original.RdpSettings, original.Notes),
            (copy.Folder, copy.Host, copy.Port, copy.Domain, copy.UserName, copy.RdpSettings, copy.Notes));
        var account = Assert.Single(copy.Accounts);
        Assert.NotEqual(ops.Id, account.Id);
        Assert.Equal("ops", account.UserName);

        // Nothing is stored until it is saved; then it has the original's passwords, bound to its own ids (NFR-11).
        Assert.Single(await ws.ListAsync());
        var saved = await ws.SaveAsync(copy, SecretUpdate.Keep);
        Assert.Equal(2, (await ws.ListAsync()).Count);
        Assert.Equal("pw", ws.RevealPassword(saved));
        Assert.Equal("ops-pw", ws.RevealPassword(saved, account.Id));
        Assert.Equal("pw", ws.RevealPassword(original));
    }

    [Fact]
    public async Task Duplicate_of_an_entry_kept_on_this_pc_stays_on_this_pc()
    {
        using var remote = new TempDb();
        using var cache = new TempDb();
        using var vault = new TempDb();
        using var ws = HarborWorkspace.CreateRemote(Users.Alice, cache.Store, remote.Store, AliceLocal, Shared, Resolver, true, vault.Store);
        await ws.OpenAsync();
        var mine = await ws.SaveAsync(ConnectionEntry.CreateNew("lab") with { Host = "lab", IsLocalOnly = true }, SecretUpdate.Set("pw"));

        var saved = await ws.SaveAsync(ws.Duplicate(mine, ["lab"]), SecretUpdate.Keep);

        Assert.True(saved.IsLocalOnly);
        Assert.Equal("pw", ws.RevealPassword(saved));
        Assert.Empty(await remote.Store.ListAsync(includeDeleted: true));
    }
}
