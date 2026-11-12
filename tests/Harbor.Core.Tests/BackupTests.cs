using System.Text;
using Harbor.Core.Backup;
using Harbor.Core.Model;

namespace Harbor.Core.Tests;

// SPEC FR-68, ADR-0046: export / restore the catalog through a passphrase-encrypted backup file.
public class BackupTests
{
    private const string Passphrase = "correct horse battery";
    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private static ConnectionEntry Web() => ConnectionEntry.CreateNew("web01") with
    {
        Folder = "Production/Web",
        Host = "10.0.0.5",
        Port = 3390,
        Domain = "CONTOSO",
        UserName = "jdoe",
        PasswordCipher = [1],
        RdpSettings = "administrative session:i:1",
        Notes = "rack 4",
    };

    private static BackupContent Sample(out ConnectionEntry web)
    {
        var w = Web();
        web = w with { Accounts = [ConnectionAccount.CreateNew("admin2") with { PasswordCipher = [2] }] };
        var vpn = ConnectionEntry.CreateNew("vpn", EntryKind.Other) with { Host = "vpn.contoso.com", Port = 443, IsLocalOnly = true };
        var group = new SmartGroup { Name = "SQL", Rules = [new SmartGroupRule { Field = SmartGroupField.Host, Operator = SmartGroupOperator.StartsWith, Value = "sql-" }] };
        var accountId = web.Accounts[0].Id;
        return HarborBackup.Create([web, vpn], (e, a) => a is null ? $"pw-{e.Name}" : a == accountId ? "pw-admin2" : null,
            [web.Id], [group], "0.12.0", Now);
    }

    [Fact]
    public void Round_trip_keeps_entries_accounts_passwords_pins_and_smart_groups()
    {
        var content = Sample(out var web);
        var file = BackupFile.Encrypt(content, Passphrase, iterations: 1000);
        var back = BackupFile.Decrypt(file, Passphrase);

        Assert.Equal("0.12.0", back.AppVersion);
        Assert.Equal(Now, back.CreatedUtc);
        Assert.Equal(2, back.Entries.Count);
        var e = back.Entries[0];
        Assert.Equal((web.Id, "web01", "Production/Web", EntryKind.RdpConnection), (e.Id, e.Name, e.Folder, e.Kind));
        Assert.Equal(("10.0.0.5", 3390, "CONTOSO", "jdoe", "pw-web01"), (e.Host, e.Port, e.Domain, e.UserName, e.Password));
        Assert.Equal(("administrative session:i:1", "rack 4"), (e.RdpSettings, e.Notes));
        Assert.True(e.Pinned);
        Assert.False(e.LocalOnly);
        var account = Assert.Single(e.Accounts);
        Assert.Equal(("admin2", "pw-admin2"), (account.UserName, account.Password));

        var vpn = back.Entries[1];
        Assert.Equal((EntryKind.Other, 443, true, false, (string?)null), (vpn.Kind, vpn.Port, vpn.LocalOnly, vpn.Pinned, vpn.Password));
        var group = Assert.Single(back.SmartGroups);
        Assert.Equal("SQL", group.Name);
        Assert.Equal("sql-", Assert.Single(group.Rules).Value);
    }

    [Fact]
    public void Passwords_are_only_asked_for_when_set()
    {
        var asked = 0;
        var content = HarborBackup.Create([ConnectionEntry.CreateNew("no-pw")], (_, _) => { asked++; return "x"; }, [], [], null, Now);
        Assert.Equal(0, asked);
        Assert.Null(content.Entries[0].Password);
    }

    // NFR-12: nothing readable on disk but the envelope; no secret or entry name in the file.
    [Fact]
    public void File_is_encrypted_and_names_its_format()
    {
        var text = Encoding.UTF8.GetString(BackupFile.Encrypt(Sample(out _), Passphrase, iterations: 1000));

        Assert.Contains(BackupFile.FormatName, text, StringComparison.Ordinal);
        foreach (var secret in new[] { "pw-web01", "pw-admin2", "web01", "10.0.0.5", "jdoe", "SQL" })
        {
            Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Wrong_passphrase_is_refused()
    {
        var file = BackupFile.Encrypt(Sample(out _), Passphrase, iterations: 1000);
        Assert.Throws<BackupPassphraseException>(() => BackupFile.Decrypt(file, "wrong passphrase!"));
    }

    [Fact]
    public void Changed_header_is_detected()
    {
        var text = Encoding.UTF8.GetString(BackupFile.Encrypt(Sample(out _), Passphrase, iterations: 1000));
        var tampered = Encoding.UTF8.GetBytes(text.Replace("\"iterations\": 1000", "\"iterations\": 1001", StringComparison.Ordinal));
        Assert.NotEqual(text, Encoding.UTF8.GetString(tampered));
        Assert.Throws<BackupPassphraseException>(() => BackupFile.Decrypt(tampered, Passphrase));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"format\":\"something-else\",\"version\":1}")]
    [InlineData("{\"format\":\"remote-desktop-harbor-backup\",\"version\":1,\"kdf\":\"PBKDF2-SHA256\",\"iterations\":1000,\"cipher\":\"AES-256-GCM\",\"salt\":\"AA==\"}")]
    public void Other_files_are_not_backups(string text)
    {
        Assert.Throws<FormatException>(() => BackupFile.Decrypt(Encoding.UTF8.GetBytes(text), Passphrase));
    }

    [Fact]
    public void Newer_backup_versions_are_refused()
    {
        var file = "{\"format\":\"remote-desktop-harbor-backup\",\"version\":2}"u8.ToArray();
        var ex = Assert.Throws<FormatException>(() => BackupFile.Decrypt(file, Passphrase));
        Assert.Contains("newer version", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Entries_do_not_print_their_passwords()
    {
        var e = Sample(out _).Entries[0];
        Assert.DoesNotContain("pw-", e.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("pw-", e.Accounts[0].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Restore_into_an_empty_catalog_adds_everything_with_new_ids()
    {
        var content = Sample(out var web);
        var plan = HarborBackup.PlanRestore(content, [], [], localOnlyAvailable: false);

        Assert.Equal(2, plan.ToAdd.Count);
        Assert.Equal(0, plan.AlreadyPresent);
        var first = plan.ToAdd[0];
        Assert.True(first.Pinned);
        Assert.NotEqual(web.Id, first.Import.Entry.Id);
        Assert.Null(first.Import.Entry.PasswordCipher);
        Assert.Equal("pw-web01", first.Import.Password);
        var account = Assert.Single(first.Import.Entry.Accounts);
        Assert.NotEqual(web.Accounts[0].Id, account.Id);
        Assert.Equal("pw-admin2", first.Import.AccountPasswords![account.Id]);

        // Without a local vault next to a shared database, entries kept on the old PC only join the catalog.
        Assert.False(plan.ToAdd[1].Import.Entry.IsLocalOnly);
        Assert.True(HarborBackup.PlanRestore(content, [], [], localOnlyAvailable: true).ToAdd[1].Import.Entry.IsLocalOnly);

        var group = Assert.Single(plan.SmartGroupsToAdd);
        Assert.NotEqual(content.SmartGroups[0].Id, group.Id);
    }

    [Fact]
    public void Restoring_twice_adds_nothing_but_pins_the_existing_twin()
    {
        var content = Sample(out _);
        var existingWeb = Web() with { Id = Guid.NewGuid(), PasswordCipher = [9] };
        var existingVpn = ConnectionEntry.CreateNew("vpn", EntryKind.Other) with { Host = "vpn.contoso.com", Port = 443 };
        var existingGroup = new SmartGroup { Name = "sql" };

        var plan = HarborBackup.PlanRestore(content, [existingWeb, existingVpn], [existingGroup], localOnlyAvailable: true);

        Assert.Empty(plan.ToAdd);
        Assert.Equal(2, plan.AlreadyPresent);
        Assert.Equal([existingWeb.Id], plan.PinExisting);
        Assert.Empty(plan.SmartGroupsToAdd);
        Assert.Equal(1, plan.SmartGroupsPresent);
        Assert.False(plan.IsEmpty);
    }

    [Fact]
    public void Restore_repairs_odd_values()
    {
        var content = new BackupContent
        {
            Entries = [new BackupEntry { Name = " ", Host = "h", Port = 0, Kind = (EntryKind)42, Folder = "a\\b", Accounts = [new BackupAccount { UserName = "" }] }],
        };
        var entry = HarborBackup.PlanRestore(content, [], [], false).ToAdd[0].Import.Entry;
        Assert.Equal(("h", ConnectionEntry.DefaultRdpPort, EntryKind.Other, "a/b"), (entry.Name, entry.Port, entry.Kind, entry.Folder));
        Assert.Empty(entry.Accounts);
    }
}
