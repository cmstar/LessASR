using LocalAsrClient.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace LocalAsrClient.App.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly MainWindow _window;
    private readonly TrayWindowActivator _windowActivator;
    private readonly TrayContextMenu _trayMenu;
    private readonly Forms.NotifyIcon _notifyIcon;
    private System.Drawing.Icon? _trayIcon;
    private bool _disposed;

    public TrayIconService(MainWindow window)
    {
        _window = window;
        _windowActivator = new TrayWindowActivator(
            new WpfTrayWindow(window),
            new Win32TrayForegroundService());
        _trayMenu = new TrayContextMenu(ShowWindow, ExitApplication);
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = LessAsrPaths.ProductName,
            Visible = false
        };
        UpdateTrayIcon();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _notifyIcon.Visible = true;
        _notifyIcon.MouseClick += OnMouseClick;
    }

    private void OnMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            ShowWindow();
            return;
        }

        if (e.Button == Forms.MouseButtons.Right)
        {
            _trayMenu.ShowAtPointer();
        }
    }

    private void ShowWindow()
    {
        _windowActivator.RestoreAndActivate();
    }

    private void ExitApplication()
    {
        _window.AllowClose();
        System.Windows.Application.Current.Shutdown();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        _ = _window.Dispatcher.BeginInvoke(UpdateTrayIcon);
    }

    private void UpdateTrayIcon()
    {
        if (_disposed)
        {
            return;
        }

        var nextIcon = TrayIconResources.Load(TrayIconResources.SystemUsesLightTheme());
        _notifyIcon.Icon = nextIcon;
        var previousIcon = _trayIcon;
        _trayIcon = nextIcon;
        previousIcon?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _trayMenu.IsOpen = false;
        _notifyIcon.Visible = false;
        _notifyIcon.Icon = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _notifyIcon.Dispose();
    }
}
