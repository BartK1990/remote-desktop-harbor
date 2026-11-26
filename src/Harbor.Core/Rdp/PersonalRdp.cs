using Harbor.Core.Model;

namespace Harbor.Core.Rdp;

/// <summary>
/// What a user keeps for an entry on this PC only (SPEC FR-76, ADR-0054), in their own settings and never in a shared
/// database: the monitors a full screen session uses (always; monitor numbers belong to this PC), and — with
/// <see cref="OwnDisplay"/> — the entry's whole display group (mode, size, monitors, dynamic resolution, smart sizing),
/// and with <see cref="OwnResources"/> its local resources (remote audio, microphone, local drives, printers, clipboard,
/// smart cards).
/// </summary>
public sealed record PersonalRdpSettings
{
    /// <summary>The display settings below replace all of the entry's display settings on this PC.</summary>
    public bool OwnDisplay { get; init; }

    /// <summary>The audio and drive settings below replace the entry's on this PC.</summary>
    public bool OwnResources { get; init; }

    /// <summary><c>.rdp</c> lines (<c>key:type:value</c>) of the personal keys only.</summary>
    public string? Lines { get; init; }

    /// <summary>
    /// The monitors a full screen session uses, by position and size (SPEC FR-75); their Remote Desktop numbers are looked
    /// up when connecting (<see cref="MonitorSelection.Resolve"/>). <c>null</c> = all monitors.
    /// </summary>
    public List<MonitorRef>? Monitors { get; init; }

    public bool IsEmpty => !OwnDisplay && !OwnResources && string.IsNullOrWhiteSpace(Lines) && Monitors is not { Count: > 0 };

    /// <summary>Whether <paramref name="key"/> is kept on this PC with these settings.</summary>
    public bool IsPersonal(string key) => PersonalRdp.IsPersonal(key, OwnDisplay, OwnResources);
}

/// <summary>Splits an entry's <c>.rdp</c> settings into shared and personal parts and merges them again (SPEC FR-76).</summary>
public static class PersonalRdp
{
    /// <summary>The monitors a session uses: personal on every PC.</summary>
    public const string MonitorsKey = "selectedmonitors";

    /// <summary>The display group, personal with <see cref="PersonalRdpSettings.OwnDisplay"/>.</summary>
    public static IReadOnlyList<string> DisplayKeys { get; } =
        ["screen mode id", "desktopwidth", "desktopheight", "use multimon", MonitorsKey, "dynamic resolution", "smart sizing"];

    /// <summary>The local resources group, personal with <see cref="PersonalRdpSettings.OwnResources"/>.</summary>
    public static IReadOnlyList<string> ResourceKeys { get; } =
        ["audiomode", "audiocapturemode", "drivestoredirect", "redirectprinters", "redirectclipboard", "redirectsmartcards"];

    public static bool IsDisplayKey(string key) => DisplayKeys.Contains(key, StringComparer.OrdinalIgnoreCase);

    public static bool IsResourceKey(string key) => ResourceKeys.Contains(key, StringComparer.OrdinalIgnoreCase);

    private static bool IsMonitorsKey(string key) => string.Equals(key, MonitorsKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="key"/> is kept on this PC: monitors always, and the groups chosen.</summary>
    public static bool IsPersonal(string key, bool ownDisplay, bool ownResources) =>
        IsMonitorsKey(key) || (ownDisplay && IsDisplayKey(key)) || (ownResources && IsResourceKey(key));

    /// <summary>
    /// The settings an entry connects with on this PC: the shared <paramref name="shared"/> lines with the personal ones
    /// in place of theirs — the monitors line, and every line of the groups the user keeps (display, audio and drives).
    /// </summary>
    /// <remarks>
    /// Picked <see cref="PersonalRdpSettings.Monitors"/> become a <c>selectedmonitors</c> line with the numbers of
    /// <paramref name="monitors"/>, this PC's monitors now; none of them still there (or no <paramref name="monitors"/>)
    /// gives no line, so the session uses all monitors.
    /// </remarks>
    public static string? Merge(string? shared, PersonalRdpSettings? personal, IReadOnlyList<LocalMonitor>? monitors = null)
    {
        if (personal is null || personal.IsEmpty) return shared;
        Func<string, bool> replaced = personal.IsPersonal;
        var lines = RdpFile.Parse(shared ?? "").Where(l => !replaced(l.Key)).Select(l => l.Raw)
            .Concat(RdpFile.Parse(personal.Lines ?? "").Where(l => replaced(l.Key)).Select(l => l.Raw))
            .ToList();
        if (personal.Monitors is { Count: > 0 } picked)
        {
            lines.RemoveAll(l => RdpFile.Parse(l).Any(p => IsMonitorsKey(p.Key)));
            if (monitors is { Count: > 0 } && MonitorSelection.Resolve(picked, monitors) is { Count: > 0 } ids)
            {
                lines.Add($"{MonitorsKey}:s:{string.Join(',', ids.Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)))}");
            }
        }

        return lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
    }

    /// <summary><paramref name="entry"/> as it connects on this PC (see <see cref="Merge"/>).</summary>
    public static ConnectionEntry Apply(ConnectionEntry entry, PersonalRdpSettings? personal, IReadOnlyList<LocalMonitor>? monitors = null) =>
        personal is null || personal.IsEmpty || entry.Kind != EntryKind.RdpConnection
            ? entry
            : entry with { RdpSettings = Merge(entry.RdpSettings, personal, monitors) };

    /// <summary>
    /// <paramref name="personal"/> with the monitors picked stored by position and size (<paramref name="picked"/>;
    /// <c>null</c> = all) instead of a <c>selectedmonitors</c> line of numbers, which change (SPEC FR-75).
    /// </summary>
    public static PersonalRdpSettings? WithMonitors(PersonalRdpSettings? personal, IReadOnlyList<MonitorRef>? picked)
    {
        var lines = RdpFile.Parse(personal?.Lines ?? "").Where(l => !IsMonitorsKey(l.Key)).Select(l => l.Raw).ToList();
        var result = (personal ?? new PersonalRdpSettings()) with
        {
            Lines = lines.Count == 0 ? null : string.Join(Environment.NewLine, lines),
            Monitors = picked is { Count: > 0 } ? [.. picked] : null,
        };
        return result.IsEmpty ? null : result;
    }

    /// <summary>
    /// Splits the settings edited in the form (<paramref name="edited"/>, as this PC connects) into what is stored in the
    /// entry and what is kept for this PC. The monitors line is always personal; with <paramref name="ownDisplay"/> or
    /// <paramref name="ownResources"/> that group's lines are personal too, and the entry keeps the lines of that group it
    /// had (<paramref name="sharedBefore"/>), so other users are not affected.
    /// </summary>
    public static (string? Shared, PersonalRdpSettings? Personal) Split(string? edited, bool ownDisplay, string? sharedBefore,
        bool ownResources = false)
    {
        var lines = RdpFile.Parse(edited ?? "").ToList();
        bool Personal(string key) => IsPersonal(key, ownDisplay, ownResources);

        // The entry's own lines of the personal groups stay as they were, except monitors, which are never shared.
        var keep = lines.Where(l => !Personal(l.Key)).Select(l => l.Raw)
            .Concat(RdpFile.Parse(sharedBefore ?? "").Where(l => Personal(l.Key) && !IsMonitorsKey(l.Key)).Select(l => l.Raw));

        var shared = keep.ToList();
        var mine = lines.Where(l => Personal(l.Key)).Select(l => l.Raw).ToList();
        var settings = new PersonalRdpSettings
        {
            OwnDisplay = ownDisplay,
            OwnResources = ownResources,
            Lines = mine.Count == 0 ? null : string.Join(Environment.NewLine, mine),
        };
        return (shared.Count == 0 ? null : string.Join(Environment.NewLine, shared), settings.IsEmpty ? null : settings);
    }
}
