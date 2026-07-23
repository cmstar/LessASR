using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LocalAsrClient.App.Tray;

internal interface ITrayWindow
{
    bool IsMinimized { get; }

    IntPtr Handle { get; }

    void Restore();

    void Show();

    void Activate();
}

internal interface ITrayForegroundService
{
    void RequestForeground(IntPtr handle);
}

internal sealed class TrayWindowActivator(
    ITrayWindow window,
    ITrayForegroundService foregroundService)
{
    public void RestoreAndActivate()
    {
        if (window.IsMinimized)
        {
            window.Restore();
        }

        window.Show();
        window.Activate();
        foregroundService.RequestForeground(window.Handle);
    }
}

internal sealed class WpfTrayWindow(MainWindow window) : ITrayWindow
{
    public bool IsMinimized => window.WindowState == WindowState.Minimized;

    public IntPtr Handle => new WindowInteropHelper(window).Handle;

    public void Restore()
    {
        window.WindowState = WindowState.Normal;
    }

    public void Show()
    {
        window.Show();
    }

    public void Activate()
    {
        window.Activate();
    }
}

internal sealed class Win32TrayForegroundService : ITrayForegroundService
{
    private const int SwRestore = 9;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNoTopmost = new(-2);

    public void RequestForeground(IntPtr handle)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        ShowWindow(handle, SwRestore);
        if (SetForegroundWindow(handle))
        {
            return;
        }

        var flags = SwpNoMove | SwpNoSize | SwpShowWindow;
        SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, flags);
        SetWindowPos(handle, HwndNoTopmost, 0, 0, 0, 0, flags);
        SetForegroundWindow(handle);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);
}
