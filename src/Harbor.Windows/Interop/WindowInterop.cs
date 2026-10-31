using System.Runtime.InteropServices;

namespace Harbor.Windows.Interop;

/// <summary>
/// Win32 messages and calls the app's own title bar needs to keep the Windows 11 snap layouts on its Maximize button
/// (SPEC FR-52, ADR-0029): the button is reported to Windows as <see cref="HitTestMaxButton"/>.
/// </summary>
public static partial class WindowInterop
{
    public const int WmNcHitTest = 0x0084;
    public const int WmNcMouseMove = 0x00A0;
    public const int WmNcLButtonDown = 0x00A1;
    public const int WmNcLButtonUp = 0x00A2;
    public const int WmNcLButtonDblClk = 0x00A3;
    public const int WmNcMouseLeave = 0x02A2;
    public const int HitTestMaxButton = 9;

    private const uint TME_LEAVE = 0x00000002;
    private const uint TME_NONCLIENT = 0x00000010;

    [StructLayout(LayoutKind.Sequential)]
    private struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public uint dwFlags;
        public nint hwndTrack;
        public uint dwHoverTime;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TrackMouseEvent(ref TRACKMOUSEEVENT lpEventTrack);

    /// <summary>Asks Windows for a <see cref="WmNcMouseLeave"/> when the mouse leaves the non-client part of <paramref name="hwnd"/>.</summary>
    public static void TrackNonClientMouseLeave(nint hwnd)
    {
        var request = new TRACKMOUSEEVENT
        {
            cbSize = (uint)Marshal.SizeOf<TRACKMOUSEEVENT>(),
            dwFlags = TME_LEAVE | TME_NONCLIENT,
            hwndTrack = hwnd,
        };
        TrackMouseEvent(ref request);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WTA_OPTIONS
    {
        public uint dwFlags;
        public uint dwMask;
    }

    private const int WTA_NONCLIENT = 1;
    private const uint WTNCA_NODRAWCAPTION = 0x1, WTNCA_NODRAWICON = 0x2, WTNCA_NOSYSMENU = 0x4;

    [LibraryImport("uxtheme.dll")]
    private static partial int SetWindowThemeAttribute(nint hwnd, int attribute, ref WTA_OPTIONS options, uint size);

    /// <summary>
    /// Stops Windows from drawing its own caption, icon and caption buttons in a window whose frame is extended into
    /// the client area (WindowChrome with <c>GlassFrameThickness=-1</c>); otherwise they show through under the app's
    /// title bar. Resizing, snapping and the system menu (Alt+Space) are unaffected.
    /// </summary>
    public static void HideSystemCaption(nint hwnd)
    {
        var options = new WTA_OPTIONS { dwFlags = WTNCA_NODRAWCAPTION | WTNCA_NODRAWICON | WTNCA_NOSYSMENU };
        options.dwMask = options.dwFlags;
        _ = SetWindowThemeAttribute(hwnd, WTA_NONCLIENT, ref options, (uint)Marshal.SizeOf<WTA_OPTIONS>());
    }

    /// <summary>Screen coordinates (device pixels) packed into a mouse message's <c>lParam</c>.</summary>
    public static (int X, int Y) ScreenPoint(nint lParam)
    {
        var value = unchecked((int)(long)lParam);
        return ((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
    }
}
