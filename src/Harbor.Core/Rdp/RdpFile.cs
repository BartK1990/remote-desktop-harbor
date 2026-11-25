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
                    (host, var p) = EntryInput.SplitHostPort(value);
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

    /// <summary>
    /// Produces the .rdp file used to launch <paramref name="entry"/>. <paramref name="overrides"/> are lines that win over
    /// the defaults and the entry's settings (e.g. drives shared for a file transfer, SPEC FR-63); managed keys are ignored.
    /// </summary>
    public static string Build(ConnectionEntry entry, IReadOnlyDictionary<string, string>? defaults = null, IEnumerable<string>? overrides = null)
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

        foreach (var (key, _, _, raw) in Parse(string.Join("\n", overrides ?? [])))
        {
            if (!ManagedKeys.Contains(key)) Set(key, raw);
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

    // Characters Windows does not allow in a file name (on any OS, so the result is the same in tests on Linux).
    private static readonly char[] InvalidFileNameChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    private const int MaxFileNameLength = 100;

    /// <summary>
    /// Name (without folder) of the temporary .rdp file for <paramref name="entry"/>, e.g. <c>Web 01.rdp</c> (SPEC FR-71).
    /// mstsc.exe titles its window <c>&lt;file name&gt; - &lt;host&gt; - Remote Desktop Connection</c>, so the file is named
    /// after the entry and the title reads <c>Web 01 - 10.0.0.5 - …</c>. mstsc cuts the file name at its first dot, so dots
    /// become the look-alike one dot leader (U+2024). An entry without a name of its own (none, or the host) gives
    /// <c>Remote Desktop Harbor</c>, as the host follows anyway.
    /// </summary>
    public static string LaunchFileName(ConnectionEntry entry)
    {
        var host = entry.Host?.Trim() ?? "";
        var name = entry.Name?.Trim() ?? "";
        if (string.Equals(name, host, StringComparison.OrdinalIgnoreCase)) name = "";

        var chars = name.Select(c => c == '.' ? DotLeader
            : char.IsControl(c) || Array.IndexOf(InvalidFileNameChars, c) >= 0 ? '_' : c).ToArray();
        var safe = new string(chars);
        if (safe.Length > MaxFileNameLength) safe = safe[..MaxFileNameLength];
        safe = safe.Trim().TrimEnd(DotLeader, ' ');
        return (safe.Length == 0 ? "Remote Desktop Harbor" : safe) + ".rdp";
    }

    // Looks like a full stop, but mstsc does not take it for the start of the file extension.
    private const char DotLeader = '\u2024';

    /// <summary>Target name mstsc.exe looks up in Credential Manager for <paramref name="host"/>.</summary>
    public static string CredentialTarget(string host) => $"TERMSRV/{host}";

    internal static IEnumerable<(string Key, string Type, string Value, string Raw)> Parse(string text)
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
}
