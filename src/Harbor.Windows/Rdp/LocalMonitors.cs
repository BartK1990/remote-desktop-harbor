using System.Diagnostics;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Harbor.Core.Rdp;

namespace Harbor.Windows.Rdp;

/// <summary>This PC's monitors, for the monitor picker of the entry editor (SPEC FR-75).</summary>
[SupportedOSPlatform("windows")]
public static class LocalMonitors
{
    /// <summary>
    /// This PC's monitors now, with Remote Desktop's numbers (from the device names, <c>\\.\DISPLAY5</c> = 4, as
    /// <c>mstsc /l</c> lists them) and their bounds in pixels, by number.
    /// </summary>
    public static IReadOnlyList<LocalMonitor> Current() =>
    [
        .. Screen.AllScreens.Select((s, i) => new LocalMonitor(MonitorSelection.IdFromDeviceName(s.DeviceName) ?? i,
                s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height, s.Primary))
            .OrderBy(m => m.Id),
    ];

    /// <summary>
    /// Shows Remote Desktop Connection's own list of this PC's monitors and their ids (<c>mstsc /l</c>), the numbers
    /// <c>selectedmonitors</c> uses. Nothing connects.
    /// </summary>
    public static void ShowIds()
    {
        var mstsc = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "mstsc.exe");
        using var process = Process.Start(new ProcessStartInfo(mstsc) { ArgumentList = { "/l" }, UseShellExecute = false });
    }
}
