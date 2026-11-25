using System.Diagnostics;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Harbor.Windows.Rdp;

/// <summary>This PC's monitors, for the monitor picker of the entry editor (SPEC FR-75).</summary>
[SupportedOSPlatform("windows")]
public static class LocalMonitors
{
    /// <summary>How many monitors this PC has now (at least 1).</summary>
    public static int Count() => Math.Max(1, Screen.AllScreens.Length);

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
