using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using Harbor.Core.Rdp;

namespace Harbor.Windows.Rdp;

/// <summary>This PC's monitors, for the monitor picker of the entry editor and for connecting (SPEC FR-75).</summary>
[SupportedOSPlatform("windows")]
public static partial class LocalMonitors
{
    private const int MaxDisplayDevices = 64;

    /// <summary>
    /// This PC's monitors now, with Remote Desktop's numbers (as <c>mstsc /l</c> lists them: the place of each monitor's
    /// display device in Windows' list of display devices, see <see cref="MonitorSelection.IdFor"/>) and their bounds in
    /// pixels, by number.
    /// </summary>
    public static IReadOnlyList<LocalMonitor> Current()
    {
        var devices = DisplayDevices();
        return
        [
            .. Screen.AllScreens.Select((s, i) => new LocalMonitor(MonitorSelection.IdFor(s.DeviceName, devices) ?? i,
                    s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height, s.Primary))
                .OrderBy(m => m.Id),
        ];
    }

    /// <summary>The names of Windows' display devices (<c>\\.\DISPLAY1</c>…), attached or not, in their own order.</summary>
    private static unsafe List<string> DisplayDevices()
    {
        var names = new List<string>();
        var device = new DisplayDevice { cb = sizeof(DisplayDevice) };
        for (uint i = 0; i < MaxDisplayDevices && EnumDisplayDevices(null, i, &device, 0); i++)
        {
            names.Add(new string(device.DeviceName));
            device = new DisplayDevice { cb = sizeof(DisplayDevice) };
        }

        return names;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct DisplayDevice
    {
        public int cb;
        public fixed char DeviceName[32];
        public fixed char DeviceString[128];
        public int StateFlags;
        public fixed char DeviceID[128];
        public fixed char DeviceKey[128];
    }

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumDisplayDevices(string? device, uint deviceNumber, DisplayDevice* info, uint flags);
}
