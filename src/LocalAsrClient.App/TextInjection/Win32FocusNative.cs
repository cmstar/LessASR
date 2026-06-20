using System.Runtime.InteropServices;

namespace LocalAsrClient.App.TextInjection;

internal static class Win32FocusNative
{
    public const int GwlStyle = -16;
    public const int EsReadOnly = 0x08000000;

    public const uint WmChar = 0x0102;
    public const uint WmGetText = 0x000D;
    public const uint WmGetTextLength = 0x000E;
    public const uint EmGetReadOnly = 0x00CF;
    public const uint EmGetTextLength = 0x00BA;
    public const uint EmGetTextRange = 0x00B8;
    public const uint EmReplaceSel = 0x00C2;
    public const int WmUser = 0x0400;
    public const int SciGetTextLength = 2180;
    public const int SciGetText = 2182;
    public const int SciReplaceSel = 2170;
    public const int SciGetCurrentPos = 2008;
    public const int SciInsertText = 2003;
    public const int AsfwAny = -1;
    public const uint SwpNoActivate = 0x0010;
    public const uint SwpShowWindow = 0x0040;
    public const uint SwpNomove = 0x0002;
    public const uint SwpNosize = 0x0001;
    public static readonly IntPtr HwndTopmost = new(-1);

    public delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct GuiThreadInfo
    {
        public int CbSize;
        public int Flags;
        public IntPtr HwndActive;
        public IntPtr HwndFocus;
        public IntPtr HwndCapture;
        public IntPtr HwndMenuOwner;
        public IntPtr HwndMoveSize;
        public IntPtr HwndCaret;
        public RECT RcCaret;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CharRange
    {
        public int CpMin;
        public int CpMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TextRange
    {
        public CharRange Chrg;
        public IntPtr LpstrText;
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetFocus();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    public static extern IntPtr SendMessageString(IntPtr hWnd, uint msg, IntPtr wParam, string? lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")]
    public static extern IntPtr SendMessageGetText(IntPtr hWnd, uint msg, IntPtr wParam, System.Text.StringBuilder lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, ref TextRange lParam);

    [DllImport("user32.dll")]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GuiThreadInfo lpgui);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AllowSetForegroundWindow(int dwProcessId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);
}
