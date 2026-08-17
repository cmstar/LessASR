using System.Runtime.InteropServices;

namespace LocalAsrClient.App.Hotkeys;

internal static class Win32HotkeyNative
{
    public const int WhKeyboardLl = 13;
    public const int WmKeyDown = 0x0100;
    public const int WmKeyUp = 0x0101;
    public const int WmSysKeyDown = 0x0104;
    public const int WmSysKeyUp = 0x0105;
    public const int VkShift = 0x10;
    public const int VkControl = 0x11;
    public const int VkMenu = 0x12;
    public const int VkLShift = 0xA0;
    public const int VkRShift = 0xA1;
    public const int VkLControl = 0xA2;
    public const int VkRMenu = 0xA5;
    public const int VkRControl = 0xA3;
    public const int VkLMenu = 0xA4;
    public const int VkLWin = 0x5B;
    public const int VkRWin = 0x5C;
    public const int VkEscape = 0x1B;
    public const int VkF9 = 0x78;

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KbdLlHookStruct
    {
        public int VkCode;
        public int ScanCode;
        public int Flags;
        public int Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc callback, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? moduleName);

    public static bool IsModifierKey(int virtualKeyCode)
    {
        return virtualKeyCode is
            VkShift or VkControl or VkMenu or
            VkLShift or VkRShift or
            VkLControl or VkRControl or
            VkLMenu or VkRMenu or
            VkLWin or VkRWin;
    }
}
