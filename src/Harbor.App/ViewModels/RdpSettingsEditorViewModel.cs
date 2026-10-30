using CommunityToolkit.Mvvm.ComponentModel;
using Harbor.Core.Rdp;

namespace Harbor.App.ViewModels;

/// <summary>A labelled value for a combo box.</summary>
public sealed record Choice<T>(string Label, T Value);

/// <summary>
/// The RDP settings of an entry as a form (SPEC FR-13, ADR-0022). Parsing and writing the <c>.rdp</c> lines is done by
/// <see cref="RdpOptions"/>; this class only maps it to bindable properties.
/// </summary>
public sealed partial class RdpSettingsEditorViewModel : ObservableObject
{
    /// <summary>The resolution choice that shows the width and height boxes.</summary>
    public static readonly Choice<RdpResolution?> CustomResolution = new("Custom…", null);

    private static readonly Choice<RdpResolution?> MatchScreen = new("Match local screen", null);

    public RdpSettingsEditorViewModel(string? rdpSettings) => Load(rdpSettings);

    public IReadOnlyList<Choice<RdpDisplayMode>> DisplayModes { get; } =
        [new("Full screen", RdpDisplayMode.FullScreen), new("Window", RdpDisplayMode.Window)];

    public IReadOnlyList<Choice<RdpResolution?>> Resolutions { get; } =
        [MatchScreen, .. RdpOptions.CommonResolutions.Select(r => new Choice<RdpResolution?>(r.ToString(), r)), CustomResolution];

    public IReadOnlyList<Choice<RdpColorDepth>> ColorDepths { get; } =
    [
        new("Highest quality (32 bit)", RdpColorDepth.Highest32), new("True colour (24 bit)", RdpColorDepth.TrueColor24),
        new("High colour (16 bit)", RdpColorDepth.HighColor16), new("High colour (15 bit)", RdpColorDepth.HighColor15),
    ];

    public IReadOnlyList<Choice<RdpAudioMode>> AudioModes { get; } =
    [
        new("Play on this computer", RdpAudioMode.ThisComputer), new("Play on remote computer", RdpAudioMode.RemoteComputer),
        new("Do not play", RdpAudioMode.DoNotPlay),
    ];

    public IReadOnlyList<Choice<RdpKeyboardMode>> KeyboardModes { get; } =
    [
        new("Only when using the full screen", RdpKeyboardMode.FullScreenOnly), new("On the remote computer", RdpKeyboardMode.RemoteComputer),
        new("On this computer", RdpKeyboardMode.ThisComputer),
    ];

    public IReadOnlyList<Choice<RdpServerAuthentication>> ServerAuthentications { get; } =
    [
        new("Warn me", RdpServerAuthentication.Warn), new("Do not connect", RdpServerAuthentication.DoNotConnect),
        new("Connect without warning", RdpServerAuthentication.ConnectWithoutWarning),
    ];

    [ObservableProperty] private RdpDisplayMode _displayMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomResolution))]
    private Choice<RdpResolution?> _resolution = MatchScreen;

    [ObservableProperty] private int _customWidth = 1920;
    [ObservableProperty] private int _customHeight = 1080;
    [ObservableProperty] private bool _useAllMonitors;
    [ObservableProperty] private bool _dynamicResolution;
    [ObservableProperty] private bool _smartSizing;
    [ObservableProperty] private RdpColorDepth _colorDepth;
    [ObservableProperty] private RdpAudioMode _audio;
    [ObservableProperty] private bool _recordAudio;
    [ObservableProperty] private bool _redirectClipboard;
    [ObservableProperty] private bool _redirectPrinters;
    [ObservableProperty] private bool _redirectSmartCards;
    [ObservableProperty] private bool _redirectDrives;
    [ObservableProperty] private RdpKeyboardMode _keyboard;
    [ObservableProperty] private bool _autoReconnect;
    [ObservableProperty] private RdpServerAuthentication _serverAuthentication;
    [ObservableProperty] private bool _adminSession;

    /// <summary>The <c>.rdp</c> lines the form does not cover, kept as text.</summary>
    [ObservableProperty] private string? _otherSettings;

    /// <summary>Opens the "other settings" box when there are any, so imported lines are not hidden.</summary>
    [ObservableProperty] private bool _showOtherSettings;

    public bool IsCustomResolution => Resolution == CustomResolution;

    /// <summary>Replaces every field with <paramref name="rdpSettings"/>, e.g. after importing an .rdp file.</summary>
    public void Load(string? rdpSettings)
    {
        var (o, other) = RdpOptions.Parse(rdpSettings);
        DisplayMode = o.DisplayMode;
        if (o.Resolution is { } r)
        {
            Resolution = Resolutions.FirstOrDefault(c => c.Value == r) ?? CustomResolution;
            CustomWidth = r.Width;
            CustomHeight = r.Height;
        }
        else
        {
            Resolution = MatchScreen;
        }

        UseAllMonitors = o.UseAllMonitors;
        DynamicResolution = o.DynamicResolution;
        SmartSizing = o.SmartSizing;
        ColorDepth = o.ColorDepth;
        Audio = o.Audio;
        RecordAudio = o.RecordAudio;
        RedirectClipboard = o.RedirectClipboard;
        RedirectPrinters = o.RedirectPrinters;
        RedirectSmartCards = o.RedirectSmartCards;
        RedirectDrives = o.RedirectDrives;
        Keyboard = o.Keyboard;
        AutoReconnect = o.AutoReconnect;
        ServerAuthentication = o.ServerAuthentication;
        AdminSession = o.AdminSession;
        OtherSettings = other;
        ShowOtherSettings = other.Length > 0;
    }

    /// <summary>An error message for the form, or <c>null</c> when it can be saved.</summary>
    public string? Validate() =>
        IsCustomResolution && !(RdpOptions.IsDesktopSize(CustomWidth) && RdpOptions.IsDesktopSize(CustomHeight))
            ? $"Width and height must be between {RdpOptions.MinDesktopSize} and {RdpOptions.MaxDesktopSize}."
            : null;

    /// <summary>The entry's <c>.rdp</c> settings text: the form's non-default values, then the other lines.</summary>
    public string Build() => RdpOptions.Format(new RdpOptions
    {
        DisplayMode = DisplayMode,
        Resolution = IsCustomResolution ? new RdpResolution(CustomWidth, CustomHeight) : Resolution.Value,
        UseAllMonitors = UseAllMonitors,
        DynamicResolution = DynamicResolution,
        SmartSizing = SmartSizing,
        ColorDepth = ColorDepth,
        Audio = Audio,
        RecordAudio = RecordAudio,
        RedirectClipboard = RedirectClipboard,
        RedirectPrinters = RedirectPrinters,
        RedirectSmartCards = RedirectSmartCards,
        RedirectDrives = RedirectDrives,
        Keyboard = Keyboard,
        AutoReconnect = AutoReconnect,
        ServerAuthentication = ServerAuthentication,
        AdminSession = AdminSession,
    }, OtherSettings);
}
