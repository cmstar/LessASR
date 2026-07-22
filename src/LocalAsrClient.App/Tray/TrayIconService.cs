using LocalAsrClient.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace LocalAsrClient.App.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly MainWindow _window;
    private readonly Forms.NotifyIcon _notifyIcon;
    private System.Drawing.Icon? _trayIcon;
    private bool _disposed;

    public TrayIconService(MainWindow window)
    {
        _window = window;
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = LessAsrPaths.ProductName,
            Visible = false,
            ContextMenuStrip = BuildMenu()
        };
        UpdateTrayIcon();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _notifyIcon.Visible = true;
        _notifyIcon.MouseClick += OnMouseClick;
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开窗口", null, (_, _) => ShowWindow());
        menu.Items.Add("退出程序", null, (_, _) => ExitApplication());
        return menu;
    }

    private void OnMouseClick(object? sender, Forms.MouseEventArgs e)
    {
        if (e.Button == Forms.MouseButtons.Left)
        {
            ShowWindow();
        }
    }

    private void ShowWindow()
    {
        _window.Show();
        _window.Activate();
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
        _notifyIcon.Visible = false;
        _notifyIcon.Icon = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
        _notifyIcon.Dispose();
    }
}
