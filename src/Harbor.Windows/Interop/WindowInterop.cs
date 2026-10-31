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

    /// <summary>Screen coordinates (device pixels) packed into a mouse message's <c>lParam</c>.</summary>
    public static (int X, int Y) ScreenPoint(nint lParam)
    {
        var value = unchecked((int)(long)lParam);
        return ((short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF));
    }
}
