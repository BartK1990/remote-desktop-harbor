using System.Runtime.InteropServices;

namespace Harbor.Screenshots;

/// <summary>The few Win32 calls the capture needs: find, place and focus windows, send keys, copy pixels.</summary>
internal static partial class Native
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
        public long Padding; // INPUT's union is as large as MOUSEINPUT
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public KeyboardInput Keyboard;
    }

    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const uint Unicode = 0x0004;
    internal const ushort VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkReturn = 0x0D, VkEscape = 0x1B, VkTab = 0x09,
        VkF2 = 0x71, VkF5 = 0x74, VkOemComma = 0xBC, VkDown = 0x28;

    [LibraryImport("user32.dll")]
    private static partial uint SendInput(uint count, [In] Input[] inputs, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    internal static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint hwnd, int attribute, out Rect value, int size);

    [LibraryImport("user32.dll")]
    internal static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    internal static partial int ReleaseDC(nint hwnd, nint hdc);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    internal static partial nint CreateCompatibleBitmap(nint hdc, int width, int height);

    [LibraryImport("gdi32.dll")]
    internal static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool BitBlt(nint dest, int x, int y, int width, int height, nint source, int sx, int sy, uint rop);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteDC(nint hdc);

    internal const int DwmExtendedFrameBounds = 9;
    internal const uint SrcCopy = 0x00CC0020;
    internal const uint SwpNoZOrder = 0x0004;

    /// <summary>Presses <paramref name="keys"/> together (modifiers first) and releases them in reverse order.</summary>
    internal static void Press(params ushort[] keys)
    {
        var inputs = keys.Select(k => Key(k, 0)).Concat(keys.Reverse().Select(k => Key(k, KeyUp))).ToArray();
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    internal static void Type(string text)
    {
        foreach (var c in text)
        {
            Input[] inputs = [Char(c, 0), Char(c, KeyUp)];
            SendInput(2, inputs, Marshal.SizeOf<Input>());
            Thread.Sleep(15);
        }
    }

    private static Input Key(ushort vk, uint flags) => new() { Type = InputKeyboard, Keyboard = new KeyboardInput { Vk = vk, Flags = flags } };

    private static Input Char(char c, uint flags) =>
        new() { Type = InputKeyboard, Keyboard = new KeyboardInput { Scan = c, Flags = Unicode | flags } };
}
