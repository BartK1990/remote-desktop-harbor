using System.Globalization;
using System.Text.RegularExpressions;

namespace Harbor.Core.Rdp;

/// <summary>
/// A monitor of this PC as Remote Desktop Connection sees it (SPEC FR-75): its number for <c>selectedmonitors</c>
/// (<c>mstsc /l</c>) and its bounds on the desktop in pixels.
/// </summary>
public sealed record LocalMonitor(int Id, int X, int Y, int Width, int Height, bool IsPrimary);

/// <summary>
/// A monitor picked for a session, by where it is and its size (SPEC FR-75, ADR-0054): Remote Desktop's monitor numbers
/// change when monitors are plugged into another port or dock, so the number is looked up again on every connection.
/// </summary>
public sealed record MonitorRef(int X, int Y, int Width, int Height);

/// <summary>Turns picked monitors into Remote Desktop's monitor numbers of the moment (SPEC FR-75).</summary>
public static partial class MonitorSelection
{
    /// <summary>
    /// Remote Desktop's number of a monitor from its Windows device name: <c>\\.\DISPLAY5</c> is 4 (as <c>mstsc /l</c>
    /// lists them); <c>null</c> for another name.
    /// </summary>
    public static int? IdFromDeviceName(string? deviceName) =>
        deviceName is not null && DisplayNumber().Match(deviceName) is { Success: true } m
        && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 1
            ? n - 1
            : null;

    public static MonitorRef RefOf(LocalMonitor monitor) => new(monitor.X, monitor.Y, monitor.Width, monitor.Height);

    /// <summary>
    /// The current numbers of the <paramref name="wanted"/> monitors, ascending: each is the monitor with the same bounds,
    /// or else the one of the same size closest to where it was (the layout moved); a monitor no longer there is left
    /// out. Each current monitor is used once.
    /// </summary>
    public static IReadOnlyList<int> Resolve(IReadOnlyList<MonitorRef> wanted, IReadOnlyList<LocalMonitor> current)
    {
        var free = current.ToList();
        var ids = new SortedSet<int>();
        foreach (var want in wanted.OrderByDescending(w => current.Any(c => RefOf(c) == w)))
        {
            var match = free.FirstOrDefault(c => RefOf(c) == want)
                ?? free.Where(c => c.Width == want.Width && c.Height == want.Height)
                    .OrderBy(c => Math.Abs((long)c.X - want.X) + Math.Abs((long)c.Y - want.Y)).FirstOrDefault();
            if (match is null) continue;
            free.Remove(match);
            ids.Add(match.Id);
        }

        return [.. ids];
    }

    /// <summary>
    /// How the picker names a monitor: size, where it is (primary, or left / right / above / below the primary, counted
    /// outwards when there are several) and its Remote Desktop number, e.g. <c>2560 × 1080 · 1st left · no. 4</c>.
    /// </summary>
    public static string Describe(LocalMonitor monitor, IReadOnlyList<LocalMonitor> all)
    {
        var size = $"{monitor.Width} × {monitor.Height}";
        var place = monitor.IsPrimary ? "primary" : Place(monitor, all);
        return $"{size} · {place} · no. {monitor.Id}";
    }

    private static string Place(LocalMonitor monitor, IReadOnlyList<LocalMonitor> all)
    {
        var primary = all.FirstOrDefault(m => m.IsPrimary) ?? all.OrderBy(m => m.Id).First();
        var direction = Direction(monitor, primary);
        var sameSide = all.Where(m => !m.IsPrimary && Direction(m, primary) == direction)
            .OrderBy(m => Distance(m, primary)).ToList();
        if (sameSide.Count <= 1) return direction;
        var n = sameSide.IndexOf(monitor) + 1;
        var ordinal = n switch { 1 => "1st", 2 => "2nd", 3 => "3rd", _ => $"{n}th" };
        return $"{ordinal} {direction}";
    }

    private static string Direction(LocalMonitor m, LocalMonitor primary)
    {
        var dx = (m.X + m.Width / 2.0) - (primary.X + primary.Width / 2.0);
        var dy = (m.Y + m.Height / 2.0) - (primary.Y + primary.Height / 2.0);
        return Math.Abs(dx) >= Math.Abs(dy) ? dx < 0 ? "left" : "right" : dy < 0 ? "above" : "below";
    }

    private static double Distance(LocalMonitor m, LocalMonitor primary) =>
        Math.Abs((m.X + m.Width / 2.0) - (primary.X + primary.Width / 2.0)) + Math.Abs((m.Y + m.Height / 2.0) - (primary.Y + primary.Height / 2.0));

    [GeneratedRegex(@"DISPLAY(\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DisplayNumber();
}
