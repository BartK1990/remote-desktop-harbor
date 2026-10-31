namespace Harbor.Core.Rdp;

/// <summary>One line of <see cref="RdpSummary"/>: a short label and its value.</summary>
public sealed record RdpSummaryLine(string Label, string Value);

/// <summary>
/// A compact, read-only description of an entry's <c>.rdp</c> settings for the details pane, so an engineer sees how
/// a connection will open without opening the editor. SPEC FR-47, ADR-0026
/// </summary>
public static class RdpSummary
{
    public static IReadOnlyList<RdpSummaryLine> Describe(string? rdpSettings)
    {
        var (o, other) = RdpOptions.Parse(rdpSettings);
        var lines = new List<RdpSummaryLine>
        {
            new("Display", Display(o)),
            new("Redirect", Join(Redirects(o), "nothing")),
            new("Security", o.ServerAuthentication switch
            {
                RdpServerAuthentication.ConnectWithoutWarning => "connect without warning",
                RdpServerAuthentication.DoNotConnect => "do not connect if unverified",
                _ => "warn if unverified",
            }),
        };

        var session = new List<string>();
        if (o.AdminSession) session.Add("admin");
        if (!o.AutoReconnect) session.Add("no auto-reconnect");
        if (session.Count > 0) lines.Add(new("Session", Join(session, "")));

        var otherCount = other.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
        if (otherCount > 0) lines.Add(new("Other", otherCount == 1 ? "1 more .rdp setting" : $"{otherCount} more .rdp settings"));
        return lines;
    }

    private static string Display(RdpOptions o)
    {
        var parts = new List<string> { o.DisplayMode == RdpDisplayMode.Window ? "window" : "full screen" };
        parts.Add(o.Resolution is { } r ? $"{r.Width}×{r.Height}" : "local size");
        if (o.UseAllMonitors) parts.Add("all monitors");
        if (o.ColorDepth != RdpColorDepth.Highest32) parts.Add($"{(int)o.ColorDepth}-bit");
        if (o.SmartSizing) parts.Add("smart sizing");
        return string.Join(" · ", parts);
    }

    private static IEnumerable<string> Redirects(RdpOptions o)
    {
        if (o.RedirectClipboard) yield return "clipboard";
        if (o.RedirectDrives) yield return "drives";
        if (o.RedirectPrinters) yield return "printers";
        if (o.RedirectSmartCards) yield return "smart cards";
        if (o.RecordAudio) yield return "microphone";
    }

    private static string Join(IEnumerable<string> parts, string empty)
    {
        var text = string.Join(", ", parts);
        return text.Length == 0 ? empty : text;
    }
}
