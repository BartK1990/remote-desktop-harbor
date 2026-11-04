using System.Globalization;
using System.Text;
using Harbor.Core.Model;

namespace Harbor.Core.Import;

/// <summary>An entry read from an import file, with the plaintext password to encrypt when it is saved.</summary>
/// <remarks>The password only lives in memory until <c>HarborWorkspace.SaveAsync</c> encrypts it. Never log it. NFR-12</remarks>
public sealed record ImportedEntry(ConnectionEntry Entry, string? Password)
{
    public override string ToString() => $"{Entry.Name} ({Entry.Kind})"; // keeps the password out of any accidental log
}

/// <summary>What an import file holds: the entries Harbor can store and, per connection type, how many rows it skipped.</summary>
public sealed record ImportResult(IReadOnlyList<ImportedEntry> Entries, IReadOnlyDictionary<string, int> Skipped)
{
    public int SkippedCount => Skipped.Values.Sum();
}

/// <summary>
/// Reads the CSV export of Devolutions Remote Desktop Manager (File → Export → Export entries (CSV)).
/// RDP sessions become RDP connections, "Username and password" entries become credentials-only entries; folders
/// come from the <c>Group</c> column, so folder rows themselves and every other connection type (VNC, web, VPN…)
/// are skipped. Columns are found by name, so their order and any extra columns do not matter.
/// SPEC FR-54, ADR-0032
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
        var skipped = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Skip(1))
        {
            string Col(string name) => index.TryGetValue(name, out var i) && i < row.Count ? row[i].Trim() : "";

            var type = Col("ConnectionType");
            if (row.All(string.IsNullOrWhiteSpace)) continue;

            EntryKind kind;
            if (type.Equals(RdpType, StringComparison.OrdinalIgnoreCase)) kind = EntryKind.RdpConnection;
            else if (type.Equals(CredentialType, StringComparison.OrdinalIgnoreCase)) kind = EntryKind.CredentialOnly;
            else
            {
                var key = type.Length == 0 ? "(no type)" : type;
                skipped[key] = skipped.GetValueOrDefault(key) + 1;
                continue;
            }

            entries.Add(ToEntry(kind, Col));
        }

        return new ImportResult(entries, skipped);
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

        var name = Empty(col("Name")) ?? host ?? user ?? "Imported entry";
        var rdpSettings = kind == EntryKind.RdpConnection && IsYes(col("OpenInConsole")) ? "administrative session:i:1" : null;

        var entry = ConnectionEntry.CreateNew(name, kind) with
        {
            Folder = FolderTree.Normalize(col("Group")),
            Host = host,
            Port = port,
            Domain = domain,
            UserName = user,
            RdpSettings = rdpSettings,
            Notes = Notes(col("Description"), col("Keywords")),
        };

        return new ImportedEntry(entry, Empty(col("CredentialPassword")));
    }

    private static string? Notes(string description, string keywords)
    {
        var notes = description.ReplaceLineEndings("\r\n").Trim();
        if (keywords.Length > 0)
        {
            notes = notes.Length == 0 ? $"Keywords: {keywords}" : $"{notes}\r\n\r\nKeywords: {keywords}";
        }

        return Empty(notes.ReplaceLineEndings("\r\n"));
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

    private static string Key(ConnectionEntry e) =>
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
