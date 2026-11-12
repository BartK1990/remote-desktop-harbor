using System.Globalization;
using System.Text;
using Harbor.Core.Model;

namespace Harbor.Core.Import;

/// <summary>
/// An entry read from an import file, with the plaintext passwords to encrypt when it is saved: <paramref name="Password"/>
/// for the default account, <paramref name="AccountPasswords"/> for additional accounts by id.
/// </summary>
/// <remarks>Passwords only live in memory until <c>HarborWorkspace.SaveAsync</c> encrypts them. Never log them. NFR-12</remarks>
public sealed record ImportedEntry(ConnectionEntry Entry, string? Password, IReadOnlyDictionary<Guid, string>? AccountPasswords = null)
{
    public override string ToString() => $"{Entry.Name} ({Entry.Kind})"; // keeps the password out of any accidental log
}

/// <summary>What an import file holds: the entries to store and how many folder rows were skipped.</summary>
public sealed record ImportResult(IReadOnlyList<ImportedEntry> Entries, int SkippedFolders);

/// <summary>
/// Reads the CSV export of Devolutions Remote Desktop Manager (File → Export → Export entries (CSV)).
/// RDP sessions become RDP connections, "Username and password" entries credentials-only entries and every other
/// connection type (VNC, TeamViewer, web, VPN…) an <see cref="EntryKind.Other"/> entry that names the RDM type in its
/// notes. Folders come from the <c>Group</c> column, so folder rows themselves are skipped. Columns are found by name,
/// so their order and any extra columns do not matter. SPEC FR-54, ADR-0032, ADR-0033
/// </summary>
public static class RdmCsvImport
{
    public const string RdpType = "RDP (Microsoft Remote Desktop)";
    public const string CredentialType = "Username and password";
    public const string FolderType = "Folder";

    /// <summary>Parses <paramref name="csvText"/>. Throws <see cref="FormatException"/> when it is not an RDM export.</summary>
    public static ImportResult Parse(string csvText)
    {
        var rows = Csv.Read(csvText);
        if (rows.Count == 0) throw new FormatException("The file is empty.");

        var header = rows[0];
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            index.TryAdd(header[i].Trim(), i);
        }

        if (!index.ContainsKey("ConnectionType") || !index.ContainsKey("Name"))
        {
            throw new FormatException(
                "This is not a Remote Desktop Manager CSV export: the columns ConnectionType and Name are missing.");
        }

        var entries = new List<ImportedEntry>();
        var skippedFolders = 0;
        foreach (var row in rows.Skip(1))
        {
            string Col(string name) => index.TryGetValue(name, out var i) && i < row.Count ? row[i].Trim() : "";

            var type = Col("ConnectionType");
            if (row.All(string.IsNullOrWhiteSpace)) continue;

            // Folders are rebuilt from each entry's Group; an RDM folder row has nothing else Harbor keeps.
            if (type.Equals(FolderType, StringComparison.OrdinalIgnoreCase))
            {
                skippedFolders++;
                continue;
            }

            var kind = type.Equals(RdpType, StringComparison.OrdinalIgnoreCase) ? EntryKind.RdpConnection
                : type.Equals(CredentialType, StringComparison.OrdinalIgnoreCase) ? EntryKind.CredentialOnly
                : EntryKind.Other;
            entries.Add(ToEntry(kind, Col));
        }

        return new ImportResult(entries, skippedFolders);
    }

    private static ImportedEntry ToEntry(EntryKind kind, Func<string, string> col)
    {
        string? host = Empty(col("Host"));
        var port = ConnectionEntry.DefaultRdpPort;
        if (host is not null)
        {
            var (h, p) = EntryInput.SplitHostPort(host);
            host = h;
            if (p is not null) port = p.Value;
        }

        if (int.TryParse(col("Port"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var explicitPort) && explicitPort is > 0 and <= 65535)
        {
            port = explicitPort;
        }

        var domain = Empty(col("CredentialDomain"));
        var user = Empty(col("CredentialUserName"));
        if (user is not null && domain is null)
        {
            (domain, user) = EntryInput.SplitDomainUser(user);
        }

        var password = Empty(col("CredentialPassword"));

        // Web credentials (RDM website entries): the default account when the entry has none, otherwise an extra account.
        var webDomain = Empty(col("DataEntry.WebDomain"));
        var webUser = Empty(col("DataEntry.WebUserName"));
        var webPassword = Empty(col("DataEntry.WebPassword"));
        var accounts = new List<ConnectionAccount>();
        Dictionary<Guid, string>? accountPasswords = null;
        if (webUser is not null && user is null && password is null)
        {
            (domain, user, password) = (webDomain, webUser, webPassword);
        }
        else if (webUser is not null && !(string.Equals(webUser, user, StringComparison.OrdinalIgnoreCase) && webPassword == password))
        {
            var account = ConnectionAccount.CreateNew(webUser, webDomain);
            accounts.Add(account);
            if (webPassword is not null) accountPasswords = new() { [account.Id] = webPassword };
        }

        var name = Empty(col("Name")) ?? host ?? user ?? "Imported entry";
        var rdpSettings = kind == EntryKind.RdpConnection && IsYes(col("OpenInConsole")) ? "administrative session:i:1" : null;

        // Other entries say what they are, so the user knows how to open them (ADR-0033).
        var typeLine = kind == EntryKind.Other ? $"Type: {TypeLabel(col("ConnectionType"), col("ConnectionSubType"))}" : "";
        var url = Empty(col("URL"));

        var entry = ConnectionEntry.CreateNew(name, kind) with
        {
            Folder = FolderTree.Normalize(col("Group")),
            Host = host,
            Port = port,
            Domain = domain,
            UserName = user,
            RdpSettings = rdpSettings,
            Notes = Notes(typeLine, url is null ? "" : $"URL: {url}", col("Description"),
                col("Keywords") is { Length: > 0 } keywords ? $"Keywords: {keywords}" : ""),
            Accounts = accounts,
        };

        return new ImportedEntry(entry, password, accountPasswords);
    }

    private static string TypeLabel(string type, string subType) =>
        type.Length == 0 ? "unknown" : subType.Length == 0 || subType.Equals("Default", StringComparison.OrdinalIgnoreCase) ? type : $"{type} ({subType})";

    // Header lines (type, URL) first, then the description, then the keywords; blank lines between the parts.
    private static string? Notes(string typeLine, string urlLine, string description, string keywordsLine)
    {
        var head = string.Join("\r\n", new[] { typeLine, urlLine }.Where(s => s.Length > 0));
        var parts = new[] { head, description.ReplaceLineEndings("\r\n").Trim(), keywordsLine.ReplaceLineEndings("\r\n") }
            .Where(s => s.Length > 0);
        return Empty(string.Join("\r\n\r\n", parts));
    }

    /// <summary>
    /// Leaves out entries the catalog already has — same kind, name, folder, host, port and user (ignoring case) — so
    /// importing the same export twice adds nothing. Rows repeated within the file are all kept, as RDM had them.
    /// </summary>
    public static IReadOnlyList<ImportedEntry> WithoutExisting(IEnumerable<ImportedEntry> entries, IEnumerable<ConnectionEntry> existing)
    {
        var known = new HashSet<string>(existing.Select(Key), StringComparer.OrdinalIgnoreCase);
        return entries.Where(e => !known.Contains(Key(e.Entry))).ToList();
    }

    /// <summary>What makes two entries the same for imports and restores (SPEC FR-54, FR-68).</summary>
    internal static string Key(ConnectionEntry e) =>
        string.Join('\u001f', (int)e.Kind, e.Name, FolderTree.Normalize(e.Folder), e.Host, e.Port, e.QualifiedUserName);

    private static bool IsYes(string value) =>
        value.Equals("Yes", StringComparison.OrdinalIgnoreCase) || value.Equals("True", StringComparison.OrdinalIgnoreCase) || value == "1";

    private static string? Empty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>RFC 4180 reader: quoted fields may hold the delimiter, doubled quotes and line breaks.</summary>
    internal static class Csv
    {
        public static List<List<string>> Read(string text)
        {
            text = text.TrimStart('﻿');
            var delimiter = DetectDelimiter(text);
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            var quoted = false;
            var any = false;

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }

                    continue;
                }

                switch (c)
                {
                    case '"':
                        quoted = true;
                        any = true;
                        break;
                    case '\r':
                        break;
                    case '\n':
                        row.Add(field.ToString());
                        field.Clear();
                        rows.Add(row);
                        row = [];
                        any = false;
                        break;
                    default:
                        if (c == delimiter)
                        {
                            row.Add(field.ToString());
                            field.Clear();
                        }
                        else
                        {
                            field.Append(c);
                        }

                        any = true;
                        break;
                }
            }

            if (any || field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }

            return rows;
        }

        // Some locales export with ';'. Decide on the header line, which never contains quotes in an RDM export.
        private static char DetectDelimiter(string text)
        {
            var end = text.IndexOf('\n');
            var header = end < 0 ? text : text[..end];
            return header.Count(c => c == ';') > header.Count(c => c == ',') ? ';' : ',';
        }
    }
}
