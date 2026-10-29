using System.Globalization;
using System.Text;

namespace Harbor.Core.Rdp;

/// <summary>Full screen or a window (<c>screen mode id</c>).</summary>
public enum RdpDisplayMode
{
    Window = 1,
    FullScreen = 2,
}

/// <summary>Colour depth of the remote session (<c>session bpp</c>).</summary>
public enum RdpColorDepth
{
    HighColor15 = 15,
    HighColor16 = 16,
    TrueColor24 = 24,
    Highest32 = 32,
}

/// <summary>Where remote audio plays (<c>audiomode</c>).</summary>
public enum RdpAudioMode
{
    ThisComputer = 0,
    RemoteComputer = 1,
    DoNotPlay = 2,
}

/// <summary>Where Windows key combinations such as Alt+Tab apply (<c>keyboardhook</c>).</summary>
public enum RdpKeyboardMode
{
    ThisComputer = 0,
    RemoteComputer = 1,
    FullScreenOnly = 2,
}

/// <summary>What happens when the server's identity cannot be verified (<c>authentication level</c>).</summary>
public enum RdpServerAuthentication
{
    ConnectWithoutWarning = 0,
    DoNotConnect = 1,
    Warn = 2,
}

/// <summary>A desktop size offered in the editor.</summary>
public sealed record RdpResolution(int Width, int Height)
{
    public override string ToString() => $"{Width} × {Height}";
}

/// <summary>
/// The common <c>.rdp</c> settings of an entry as typed fields, so the editor can show them as a form instead of raw
/// text. Lines the form does not cover (unknown keys, or values it cannot show) are kept verbatim as "other" settings.
/// Defaults are what a launch uses when the key is absent (<see cref="RdpFile.DefaultSettings"/>, else mstsc's own), so
/// only settings that differ from them are written. SPEC FR-13, ADR-0022.
/// </summary>
public sealed record RdpOptions
{
    /// <summary>The limits mstsc accepts for <c>desktopwidth</c> and <c>desktopheight</c>.</summary>
    public const int MinDesktopSize = 200, MaxDesktopSize = 8192;

    public static RdpOptions Default { get; } = new();

    public static IReadOnlyList<RdpResolution> CommonResolutions { get; } =
    [
        new(1024, 768), new(1280, 720), new(1280, 1024), new(1366, 768), new(1440, 900), new(1600, 900),
        new(1680, 1050), new(1920, 1080), new(1920, 1200), new(2560, 1440), new(3840, 2160),
    ];

    public RdpDisplayMode DisplayMode { get; init; } = RdpDisplayMode.FullScreen;

    /// <summary>Remote desktop size; <c>null</c> matches the local screen or window.</summary>
    public RdpResolution? Resolution { get; init; }

    public bool UseAllMonitors { get; init; }
    public bool DynamicResolution { get; init; } = true;
    public bool SmartSizing { get; init; }
    public RdpColorDepth ColorDepth { get; init; } = RdpColorDepth.Highest32;

    public RdpAudioMode Audio { get; init; } = RdpAudioMode.ThisComputer;
    public bool RecordAudio { get; init; }
    public bool RedirectClipboard { get; init; } = true;
    public bool RedirectPrinters { get; init; } = true;
    public bool RedirectSmartCards { get; init; } = true;

    /// <summary>All local drives (<c>drivestoredirect:s:*</c>). A selection of drives stays an "other" line.</summary>
    public bool RedirectDrives { get; init; }

    public RdpKeyboardMode Keyboard { get; init; } = RdpKeyboardMode.FullScreenOnly;

    public bool AutoReconnect { get; init; } = true;
    public RdpServerAuthentication ServerAuthentication { get; init; } = RdpServerAuthentication.Warn;
    public bool AdminSession { get; init; }

    private sealed record Field(string Key, Func<RdpOptions, int> Get, Func<RdpOptions, int, RdpOptions> With, Func<int, bool> IsValid);

    private static Field Flag(string key, Func<RdpOptions, bool> get, Func<RdpOptions, bool, RdpOptions> with) =>
        new(key, o => get(o) ? 1 : 0, (o, v) => with(o, v == 1), v => v is 0 or 1);

    private static Field Choice<T>(string key, Func<RdpOptions, T> get, Func<RdpOptions, T, RdpOptions> with)
        where T : struct, Enum =>
        new(key, o => Convert.ToInt32(get(o), CultureInfo.InvariantCulture), (o, v) => with(o, (T)Enum.ToObject(typeof(T), v)),
            v => Enum.IsDefined(typeof(T), v));

    // Written in this order; integer (":i:") settings only.
    private static readonly Field[] Fields =
    [
        Choice("screen mode id", o => o.DisplayMode, (o, v) => o with { DisplayMode = v }),
        Flag("use multimon", o => o.UseAllMonitors, (o, v) => o with { UseAllMonitors = v }),
        Flag("dynamic resolution", o => o.DynamicResolution, (o, v) => o with { DynamicResolution = v }),
        Flag("smart sizing", o => o.SmartSizing, (o, v) => o with { SmartSizing = v }),
        Choice("session bpp", o => o.ColorDepth, (o, v) => o with { ColorDepth = v }),
        Choice("audiomode", o => o.Audio, (o, v) => o with { Audio = v }),
        Flag("audiocapturemode", o => o.RecordAudio, (o, v) => o with { RecordAudio = v }),
        Flag("redirectclipboard", o => o.RedirectClipboard, (o, v) => o with { RedirectClipboard = v }),
        Flag("redirectprinters", o => o.RedirectPrinters, (o, v) => o with { RedirectPrinters = v }),
        Flag("redirectsmartcards", o => o.RedirectSmartCards, (o, v) => o with { RedirectSmartCards = v }),
        Choice("keyboardhook", o => o.Keyboard, (o, v) => o with { Keyboard = v }),
        Flag("autoreconnection enabled", o => o.AutoReconnect, (o, v) => o with { AutoReconnect = v }),
        Choice("authentication level", o => o.ServerAuthentication, (o, v) => o with { ServerAuthentication = v }),
        Flag("administrative session", o => o.AdminSession, (o, v) => o with { AdminSession = v }),
    ];

    private const string WidthKey = "desktopwidth", HeightKey = "desktopheight", DrivesKey = "drivestoredirect";

    /// <summary>Splits an entry's <c>.rdp</c> settings into form fields and the lines the form does not cover.</summary>
    public static (RdpOptions Options, string Other) Parse(string? rdpSettings)
    {
        var options = Default;
        int? width = null, height = null;
        var other = new List<string>();

        foreach (var (key, type, value, raw) in RdpFile.Parse(rdpSettings ?? ""))
        {
            var field = Array.Find(Fields, f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
            if (field is not null && type == "i" && TryInt(value, out var v) && field.IsValid(v))
            {
                options = field.With(options, v);
            }
            else if (IsKey(key, WidthKey) && type == "i" && TryInt(value, out var w) && IsDesktopSize(w))
            {
                width = w;
            }
            else if (IsKey(key, HeightKey) && type == "i" && TryInt(value, out var h) && IsDesktopSize(h))
            {
                height = h;
            }
            else if (IsKey(key, DrivesKey) && type == "s" && value.Trim() is "*" or "")
            {
                options = options with { RedirectDrives = value.Trim() == "*" };
            }
            else
            {
                other.Add(raw);
            }
        }

        if (width is not null && height is not null)
        {
            options = options with { Resolution = new RdpResolution(width.Value, height.Value) };
        }
        else
        {
            // Half a size is not something the form can show; keep it as text.
            if (width is not null) other.Add($"{WidthKey}:i:{width}");
            if (height is not null) other.Add($"{HeightKey}:i:{height}");
        }

        return (options, string.Join(Environment.NewLine, other));
    }

    /// <summary>Writes the settings that differ from the defaults, then <paramref name="other"/> unchanged.</summary>
    public static string Format(RdpOptions options, string? other)
    {
        ArgumentNullException.ThrowIfNull(options);
        var lines = new List<string>();
        foreach (var field in Fields)
        {
            var value = field.Get(options);
            if (value != field.Get(Default)) lines.Add($"{field.Key}:i:{value.ToString(CultureInfo.InvariantCulture)}");
        }

        if (options.Resolution is { } r)
        {
            lines.Add($"{WidthKey}:i:{r.Width.ToString(CultureInfo.InvariantCulture)}");
            lines.Add($"{HeightKey}:i:{r.Height.ToString(CultureInfo.InvariantCulture)}");
        }

        if (options.RedirectDrives) lines.Add($"{DrivesKey}:s:*");

        var sb = new StringBuilder(string.Join(Environment.NewLine, lines));
        if (!string.IsNullOrWhiteSpace(other))
        {
            if (sb.Length > 0) sb.Append(Environment.NewLine);
            sb.Append(other.Trim());
        }

        return sb.ToString();
    }

    public static bool IsDesktopSize(int value) => value is >= MinDesktopSize and <= MaxDesktopSize;

    private static bool IsKey(string key, string expected) => string.Equals(key, expected, StringComparison.OrdinalIgnoreCase);

    private static bool TryInt(string value, out int result) =>
        int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
}
