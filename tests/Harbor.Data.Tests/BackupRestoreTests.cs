using Harbor.Core.Backup;
using Harbor.Core.Model;
using Harbor.Core.Security;
using Harbor.Core.Services;

namespace Harbor.Data.Tests;

// SPEC FR-68, ADR-0046: a backup moves the catalog to another PC, whose vault key is a different one (NFR-10).
public class BackupRestoreTests
{
    private static readonly TestKeyProtector OldPc = new("local-old", "old-machine");
    private static readonly TestKeyProtector NewPc = new("local-new", "new-machine");

    [Fact]
    public async Task Backup_from_one_pc_restores_on_another_with_its_own_key()
    {
        using var oldDb = new TempDb();
        using var newDb = new TempDb();
        byte[] file;
        Guid webId;
        using (var old = HarborWorkspace.CreateLocal(Users.Alice, oldDb.Store, OldPc, new KeyProtectorResolver([OldPc])))
        {
            await old.OpenAsync();
            var account = ConnectionAccount.CreateNew("admin2", "LAB");
            var web = await old.SaveAsync(ConnectionEntry.CreateNew("web01") with { Host = "web01", Folder = "Prod", UserName = "jdoe", Accounts = [account] },
                SecretUpdate.Set("pw-web"), new Dictionary<Guid, SecretUpdate> { [account.Id] = SecretUpdate.Set("pw-admin2") });
            await old.SaveAsync(ConnectionEntry.CreateNew("no-password") with { Host = "h2" }, SecretUpdate.Keep);
            webId = web.Id;

            var content = HarborBackup.Create(await old.ListAsync(), (e, a) => old.RevealPassword(e, a), [webId], [], "test", DateTimeOffset.UtcNow);
            file = BackupFile.Encrypt(content, "a long passphrase", iterations: 1000);
        }

        // The new PC cannot read the old vault, but the backup only needs the passphrase.
        using var ws = HarborWorkspace.CreateLocal(Users.Alice, newDb.Store, NewPc, new KeyProtectorResolver([NewPc]));
        await ws.OpenAsync();
        var plan = HarborBackup.PlanRestore(BackupFile.Decrypt(file, "a long passphrase"), await ws.ListAsync(), [], ws.SupportsLocalEntries);
        Assert.Equal(2, plan.ToAdd.Count);
        foreach (var item in plan.ToAdd)
        {
            await ws.SaveAsync(item.Import.Entry, item.Import.Password is { } pw ? SecretUpdate.Set(pw) : SecretUpdate.Keep,
                item.Import.AccountPasswords?.ToDictionary(p => p.Key, p => SecretUpdate.Set(p.Value)));
        }

        var restored = await ws.ListAsync();
        var restoredWeb = restored.Single(e => e.Name == "web01");
        Assert.NotEqual(webId, restoredWeb.Id);
        Assert.Equal(("web01", "Prod", "jdoe"), (restoredWeb.Host, restoredWeb.Folder, restoredWeb.UserName));
        Assert.Equal("pw-web", ws.RevealPassword(restoredWeb));
        var restoredAccount = Assert.Single(restoredWeb.Accounts);
        Assert.Equal(("LAB", "admin2"), (restoredAccount.Domain, restoredAccount.UserName));
        Assert.Equal("pw-admin2", ws.RevealPassword(restoredWeb, restoredAccount.Id));
        Assert.Null(ws.RevealPassword(restored.Single(e => e.Name == "no-password")));

        // A second restore finds everything already there.
        var again = HarborBackup.PlanRestore(BackupFile.Decrypt(file, "a long passphrase"), restored, [], ws.SupportsLocalEntries);
        Assert.Empty(again.ToAdd);
        Assert.Equal([restoredWeb.Id], again.PinExisting);
    }
}
