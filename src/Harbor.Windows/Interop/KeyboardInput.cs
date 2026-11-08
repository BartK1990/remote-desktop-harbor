using System.Runtime.InteropServices;

namespace Harbor.Windows.Interop;

/// <summary>
/// Types key combinations with <c>SendInput</c> into the window in front (SPEC FR-64, ADR-0042): the transfer window
/// types Win+R, Ctrl+V and Enter into its Remote Desktop control. Callers check that their own window is in front first,
/// so nothing is typed into another program.
/// </summary>
internal static unsafe partial class KeyboardInput
{
    public const ushort VkReturn = 0x0D;
    public const ushort VkControl = 0x11;
    public const ushort VkLeftWindows = 0x5B;
    public const ushort VkR = 0x52;
    public const ushort VkV = 0x56;

    private const uint InputKeyboard = 1;
    private const uint KeyEventExtendedKey = 0x0001;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint MapVkToVsc = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    // The union is as large as its largest member (MOUSEINPUT); SendInput checks cbSize.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint cInputs, INPUT* pInputs, int cbSize);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static partial uint MapVirtualKey(uint uCode, uint uMapType);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    /// <summary>
    /// Presses <paramref name="modifier"/>, then <paramref name="key"/>, and releases both. Scan codes are filled in:
    /// Remote Desktop sends scan codes, not virtual keys, to the server.
    /// </summary>
    public static bool Chord(ushort modifier, ushort key) => Send([(modifier, false), (key, false), (key, true), (modifier, true)]);

    public static bool Press(ushort key) => Send([(key, false), (key, true)]);

    private static bool Send(ReadOnlySpan<(ushort Key, bool Up)> keys)
    {
        var inputs = stackalloc INPUT[keys.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            var (key, up) = keys[i];
            inputs[i] = new INPUT
            {
                type = InputKeyboard,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = key,
                        wScan = (ushort)MapVirtualKey(key, MapVkToVsc),
                        dwFlags = (key == VkLeftWindows ? KeyEventExtendedKey : 0) | (up ? KeyEventKeyUp : 0),
                    },
                },
            };
        }

        return SendInput((uint)keys.Length, inputs, sizeof(INPUT)) == keys.Length;
    }
}
