using Harbor.App.Infrastructure;
using Harbor.Core.Identity;
using Harbor.Core.Model;
using Harbor.Core.Services;
using Harbor.Data;
using Harbor.Data.Providers;

namespace Harbor.Screenshots;

/// <summary>
/// The fictional catalog shown in the documentation (contoso.local, the same as the mockups), saved through the app's
/// own workspace into a demo shared database, plus presence records of two colleagues. Nothing here ships in the app.
/// </summary>
internal static class DemoCatalog
{
    public const string Password = "demo-password-not-real";

    private sealed record Item(string Name, string? Folder, string? Host, string? User, EntryKind Kind = EntryKind.RdpConnection,
        int Port = ConnectionEntry.DefaultRdpPort, string? Rdp = null, string? Notes = null, string[]? Accounts = null);

    private static readonly Item[] Items =
    [
        new("LAB-DC01", "Lab", "lab-dc01.lab.contoso.local", "administrator"),
        new("LAB-WEB01", "Lab", "lab-web01.lab.contoso.local", "administrator"),
        new("LAB-SQL01", "Lab", "lab-sql01.lab.contoso.local", "administrator"),
        new("WEB01", "Production/Web", "web01.contoso.local", "svc-web"),
        new("WEB02", "Production/Web", "web02.contoso.local", "svc-web"),
        new("WEB03 – Intranet", "Production/Web", "intranet.contoso.local", "svc-web"),
        new("APP01 – Line of business", "Production", "srv-app01.contoso.local", "svc-app",
            Notes: "Line-of-business app. Use the ops account for deployments.", Accounts: ["ops"]),
        new("APP02 – Reporting", "Production", "srv-app02.contoso.local", "svc-admin",
            Rdp: "screen mode id:i:1\r\ndesktopwidth:i:1920\r\ndesktopheight:i:1080\r\ndrivestoredirect:s:*\r\nadministrative session:i:1",
            Notes: "Reporting Services + SSRS portal. Maintenance window Sun 02:00–04:00."),
        new("DC01", "Production", "dc01.contoso.local", "admin"),
        new("DC02", "Production", "dc02.contoso.local", "admin"),
        new("SQL01", "Production", "sql01.contoso.local", "sql-admin"),
        new("SQL02 – Analysis", "Production", "10.20.4.12", "sql-admin", Port: 3390),
        new("APP01-STG", "Staging", "stg-app01.contoso.local", "svc-app"),
        new("Jump host (external)", "Staging", "jump.contoso.com", "jump"),
        new("Local admin (workstations)", null, null, @".\administrator", EntryKind.CredentialOnly),
    ];

    /// <summary>Saves the catalog; returns the saved entries by name.</summary>
    public static async Task<Dictionary<string, ConnectionEntry>> SeedAsync(AppHost host)
    {
        var saved = new Dictionary<string, ConnectionEntry>();
        foreach (var item in Items)
        {
            var accounts = (item.Accounts ?? []).Select(a => ConnectionAccount.CreateNew(a)).ToList();
            var entry = ConnectionEntry.CreateNew(item.Name, item.Kind) with
            {
                Folder = item.Folder,
                Host = item.Host,
                Port = item.Port,
                Domain = item.User?.StartsWith('.') == true ? null : "CONTOSO",
                UserName = item.User,
                RdpSettings = item.Rdp,
                Notes = item.Notes,
                Accounts = accounts,
            };
            saved[item.Name] = await host.Workspace.SaveAsync(entry, SecretUpdate.Set(Password),
                accounts.ToDictionary(a => a.Id, _ => SecretUpdate.Set(Password)));
        }

        return saved;
    }

    /// <summary>Two colleagues connected at the moment (SPEC FR-30..33): Jane on APP02, Sam on APP01 as ops.</summary>
    public static async Task AddColleaguesAsync(string connectionString, Dictionary<string, ConnectionEntry> entries)
    {
        var store = new SqlHarborStore(DatabaseProviders.Get(SqlServerProvider.ProviderName), connectionString);
        var ttl = TimeSpan.FromHours(3);
        await store.StartSessionAsync(Guid.NewGuid(), entries["APP02 – Reporting"].Id, @"CONTOSO\svc-admin",
            new UserIdentity(@"CONTOSO\jane.doe", "Jane Doe", "jane.doe@contoso.com", IdentitySource.Windows), "PC-42", ttl);
        await store.StartSessionAsync(Guid.NewGuid(), entries["APP01 – Line of business"].Id, @"CONTOSO\ops",
            new UserIdentity(@"CONTOSO\sam.patel", "Sam Patel", "sam.patel@contoso.com", IdentitySource.Windows), "PC-17", ttl);
    }
}
