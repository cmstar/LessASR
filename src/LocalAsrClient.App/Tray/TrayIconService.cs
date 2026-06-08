using LocalAsrClient.Core;
using Forms = System.Windows.Forms;

namespace LocalAsrClient.App.Tray;

public sealed class TrayIconService : IDisposable
{
    private readonly MainWindow _window;
    private readonly Forms.NotifyIcon _notifyIcon;

    public TrayIconService(MainWindow window)
    {
        _window = window;
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = LessAsrPaths.ProductName,
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
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

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
