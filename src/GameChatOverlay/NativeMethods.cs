using System;
using System.Runtime.InteropServices;

namespace GameChatOverlay;

internal static class NativeMethods
{
    internal const int HotkeyId = 0x4F43;
    internal const int WmHotkey = 0x0312;
    internal const uint HotkeyModifiers = 0x0001 | 0x0002 | 0x4000; // ALT | CTRL | NOREPEAT
    internal const uint SpaceKey = 0x20;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr window, uint flags);

    internal static IntPtr ForegroundRoot => GetAncestor(GetForegroundWindow(), 2); // GA_ROOT

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr window);
}
