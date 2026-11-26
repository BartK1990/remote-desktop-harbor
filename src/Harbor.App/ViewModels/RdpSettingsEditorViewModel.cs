using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>
    /// The monitors a full screen session may use with <see cref="UseAllMonitors"/> (SPEC FR-75): one per monitor of this
    /// PC now, named by size and place, with Remote Desktop's number. All checked = all monitors.
    /// </summary>
    public ObservableCollection<MonitorChoice> Monitors { get; } = [];

    [RelayCommand]
    private void ShowMonitorNumbers() => Infrastructure.Monitors.ShowIds?.Invoke();

    /// <summary>The monitors picked, by position and size, or <c>null</c> for all (SPEC FR-75).</summary>
    public IReadOnlyList<MonitorRef>? PickedMonitors() =>
        UseAllMonitors && Monitors.Any(m => !m.IsSelected) && Monitors.Any(m => m.IsSelected)
            ? [.. Monitors.Where(m => m.IsSelected).Select(m => MonitorSelection.RefOf(m.Monitor))]
            : null;

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

    /// <summary>
    /// The display settings (mode, size, monitors, dynamic resolution, smart sizing) are kept for this user on this PC
    /// instead of in the entry (SPEC FR-76). The monitors picked are always kept on this PC.
    /// </summary>
    [ObservableProperty] private bool _ownDisplay;

    /// <summary>Remote audio, microphone, local drives, printers and clipboard are kept for this user on this PC (SPEC FR-76).</summary>
    [ObservableProperty] private bool _ownResources;

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
        Monitors.Clear();
        var current = Infrastructure.Monitors.Current();
        foreach (var monitor in current)
        {
            Monitors.Add(new MonitorChoice(monitor, MonitorSelection.Describe(monitor, current))
            {
                IsSelected = o.SelectedMonitors.Count == 0 || o.SelectedMonitors.Contains(monitor.Id),
            });
        }

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
            : UseAllMonitors && Monitors.Count > 0 && !Monitors.Any(m => m.IsSelected)
                ? "Pick at least one monitor, or turn off \"Use several monitors\"."
                : null;

    /// <summary>The monitors picked, or none when all are (SPEC FR-75); only with <see cref="UseAllMonitors"/>.</summary>
    private IReadOnlyList<int> SelectedMonitors() =>
        UseAllMonitors && Monitors.Any(m => !m.IsSelected) ? [.. Monitors.Where(m => m.IsSelected).Select(m => m.Id)] : [];

    /// <summary>The entry's <c>.rdp</c> settings text: the form's non-default values, then the other lines.</summary>
    public string Build() => RdpOptions.Format(new RdpOptions
    {
        DisplayMode = DisplayMode,
        Resolution = IsCustomResolution ? new RdpResolution(CustomWidth, CustomHeight) : Resolution.Value,
        UseAllMonitors = UseAllMonitors,
        SelectedMonitors = SelectedMonitors(),
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

/// <summary>A monitor of this PC in the editor's monitor picker (SPEC FR-75).</summary>
public sealed partial class MonitorChoice(LocalMonitor monitor, string label) : ObservableObject
{
    public LocalMonitor Monitor { get; } = monitor;

    /// <summary>Remote Desktop's number for the monitor now.</summary>
    public int Id => Monitor.Id;

    /// <summary>Size, place and number, e.g. <c>2560 × 1080 · 1st left · no. 4</c>.</summary>
    public string Label { get; } = label;

    [ObservableProperty] private bool _isSelected;
}
