using System.Text;
using Harbor.Core.Model;

namespace Harbor.Core.Rdp;

/// <summary>
/// Reads and writes the <c>.rdp</c> text format (<c>key:type:value</c> per line) consumed by mstsc.exe.
/// Passwords are never written into .rdp files; they go through Windows Credential Manager (ADR-0008).
/// </summary>
public static class RdpFile
{
    // Keys that the app owns: derived from entry fields or forbidden (secrets).
    private static readonly HashSet<string> ManagedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "full address", "server port", "username", "domain", "password 51", "password",
    };

    public sealed record ImportResult(string? Host, int Port, string? Domain, string? UserName, string RdpSettings);

    /// <summary>Splits an existing .rdp file into entry fields and the remaining settings.</summary>
    public static ImportResult Import(string rdpText)
    {
        string? host = null, user = null, domain = null;
        var port = ConnectionEntry.DefaultRdpPort;
        var rest = new StringBuilder();

        foreach (var (key, type, value, raw) in Parse(rdpText))
        {
            switch (key.ToLowerInvariant())
            {
                case "full address":
                    (host, var p) = SplitHostPort(value);
                    if (p is not null) port = p.Value;
                    break;
                case "server port" when int.TryParse(value, out var sp):
                    port = sp;
                    break;
                case "username":
                    user = value;
                    break;
                case "domain":
                    domain = value;
                    break;
                default:
                    if (!ManagedKeys.Contains(key)) rest.AppendLine(raw);
                    break;
            }
            _ = type;
        }

        if (user is not null && domain is null && user.Contains('\\'))
        {
            var idx = user.IndexOf('\\');
            domain = user[..idx];
            user = user[(idx + 1)..];
        }

        return new ImportResult(host, port, domain, user, rest.ToString().TrimEnd());
    }

    /// <summary>Produces the .rdp file used to launch <paramref name="entry"/>.</summary>
    public static string Build(ConnectionEntry entry, IReadOnlyDictionary<string, string>? defaults = null)
    {
        if (!entry.CanLaunch)
        {
            throw new InvalidOperationException($"Entry '{entry.Name}' has no host to connect to.");
        }

        var lines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();

        void Set(string key, string line)
        {
            if (!lines.ContainsKey(key)) order.Add(key);
            lines[key] = line;
        }

        foreach (var (k, v) in defaults ?? DefaultSettings)
        {
            Set(k, v);
        }

        if (entry.Kind == EntryKind.RdpConnection && !string.IsNullOrWhiteSpace(entry.RdpSettings))
        {
            foreach (var (key, _, _, raw) in Parse(entry.RdpSettings))
            {
                if (!ManagedKeys.Contains(key)) Set(key, raw);
            }
        }

        var address = entry.Port == ConnectionEntry.DefaultRdpPort ? entry.Host! : $"{entry.Host}:{entry.Port}";
        Set("full address", $"full address:s:{address}");
        if (entry.QualifiedUserName is { } user)
        {
            Set("username", $"username:s:{user}");
        }

        var sb = new StringBuilder();
        foreach (var key in order)
        {
            sb.Append(lines[key]).Append("\r\n");
        }

        return sb.ToString();
    }

    public static IReadOnlyDictionary<string, string> DefaultSettings { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["screen mode id"] = "screen mode id:i:2",
        ["use multimon"] = "use multimon:i:0",
        ["prompt for credentials"] = "prompt for credentials:i:0",
        ["authentication level"] = "authentication level:i:2",
        ["redirectclipboard"] = "redirectclipboard:i:1",
        ["autoreconnection enabled"] = "autoreconnection enabled:i:1",
    };

    /// <summary>Target name mstsc.exe looks up in Credential Manager for <paramref name="host"/>.</summary>
    public static string CredentialTarget(string host) => $"TERMSRV/{host}";

    private static IEnumerable<(string Key, string Type, string Value, string Raw)> Parse(string text)
    {
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim().TrimStart('﻿');
            if (line.Length == 0) continue;
            var first = line.IndexOf(':');
            if (first <= 0) continue;
            var second = line.IndexOf(':', first + 1);
            if (second < 0) continue;
            yield return (line[..first].Trim(), line[(first + 1)..second], line[(second + 1)..], line);
        }
    }

    private static (string Host, int? Port) SplitHostPort(string value)
    {
        // IPv6 literal: [::1]:3390
        if (value.StartsWith('['))
        {
            var close = value.IndexOf(']');
            if (close > 0 && close + 1 < value.Length && value[close + 1] == ':' && int.TryParse(value[(close + 2)..], out var p6))
            {
                return (value[..(close + 1)], p6);
            }

            return (value, null);
        }

        var colon = value.LastIndexOf(':');
        if (colon > 0 && value.IndexOf(':') == colon && int.TryParse(value[(colon + 1)..], out var p))
        {
            return (value[..colon], p);
        }

        return (value, null);
    }
}
