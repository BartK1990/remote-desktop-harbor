using Harbor.Core.Model;

namespace Harbor.Core.Rdp;

/// <summary>
/// What a user keeps for an entry on this PC only (SPEC FR-76, ADR-0054), in their own settings and never in a shared
/// database: the monitors a full screen session uses (always; monitor numbers belong to this PC), and — with
/// <see cref="OwnDisplay"/> — the entry's whole display group (mode, size, monitors, dynamic resolution, smart sizing).
/// </summary>
public sealed record PersonalRdpSettings
{
    /// <summary>The display settings below replace all of the entry's display settings on this PC.</summary>
    public bool OwnDisplay { get; init; }

    /// <summary><c>.rdp</c> lines (<c>key:type:value</c>), display keys only.</summary>
    public string? Lines { get; init; }

    public bool IsEmpty => !OwnDisplay && string.IsNullOrWhiteSpace(Lines);
}

/// <summary>Splits an entry's <c>.rdp</c> settings into shared and personal parts and merges them again (SPEC FR-76).</summary>
public static class PersonalRdp
{
    /// <summary>The monitors a session uses: personal on every PC.</summary>
    public const string MonitorsKey = "selectedmonitors";

    /// <summary>The display group, personal with <see cref="PersonalRdpSettings.OwnDisplay"/>.</summary>
    public static IReadOnlyList<string> DisplayKeys { get; } =
        ["screen mode id", "desktopwidth", "desktopheight", "use multimon", MonitorsKey, "dynamic resolution", "smart sizing"];

    public static bool IsDisplayKey(string key) => DisplayKeys.Contains(key, StringComparer.OrdinalIgnoreCase);

    private static bool IsMonitorsKey(string key) => string.Equals(key, MonitorsKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The settings an entry connects with on this PC: the shared <paramref name="shared"/> lines with the personal ones
    /// in place of theirs — all display lines with <see cref="PersonalRdpSettings.OwnDisplay"/>, else the monitors line.
    /// </summary>
    public static string? Merge(string? shared, PersonalRdpSettings? personal)
    {
        if (personal is null || personal.IsEmpty) return shared;
        var replaced = personal.OwnDisplay ? (Func<string, bool>)IsDisplayKey : IsMonitorsKey;
        var lines = RdpFile.Parse(shared ?? "").Where(l => !replaced(l.Key)).Select(l => l.Raw)
            .Concat(RdpFile.Parse(personal.Lines ?? "").Where(l => replaced(l.Key)).Select(l => l.Raw))
            .ToList();
        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    /// <summary><paramref name="entry"/> as it connects on this PC (see <see cref="Merge"/>).</summary>
    public static ConnectionEntry Apply(ConnectionEntry entry, PersonalRdpSettings? personal) =>
        personal is null || personal.IsEmpty || entry.Kind != EntryKind.RdpConnection
            ? entry
            : entry with { RdpSettings = Merge(entry.RdpSettings, personal) };

    /// <summary>
    /// Splits the settings edited in the form (<paramref name="edited"/>, as this PC connects) into what is stored in the
    /// entry and what is kept for this PC. The monitors line is always personal. With <paramref name="ownDisplay"/> all
    /// display lines are personal and the entry keeps the display lines it had (<paramref name="sharedBefore"/>), so other
    /// users are not affected.
    /// </summary>
    public static (string? Shared, PersonalRdpSettings? Personal) Split(string? edited, bool ownDisplay, string? sharedBefore)
    {
        var lines = RdpFile.Parse(edited ?? "").ToList();
        Func<string, bool> personal = ownDisplay ? IsDisplayKey : IsMonitorsKey;
        var keep = lines.Where(l => !personal(l.Key)).Select(l => l.Raw);
        if (ownDisplay)
        {
            // The entry's own display lines stay as they were, except monitors, which are never shared.
            keep = keep.Concat(RdpFile.Parse(sharedBefore ?? "").Where(l => IsDisplayKey(l.Key) && !IsMonitorsKey(l.Key)).Select(l => l.Raw));
        }

        var shared = keep.ToList();
        var mine = lines.Where(l => personal(l.Key)).Select(l => l.Raw).ToList();
        var settings = new PersonalRdpSettings { OwnDisplay = ownDisplay, Lines = mine.Count == 0 ? null : string.Join(Environment.NewLine, mine) };
        return (shared.Count == 0 ? null : string.Join(Environment.NewLine, shared), settings.IsEmpty ? null : settings);
    }
}
