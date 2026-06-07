using System.Windows;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.Tray;

namespace LocalAsrClient.App;

public partial class App : System.Windows.Application
{
    private TrayIconService? _trayIconService;
    private MainWindow? _mainWindow;
    private AppServices? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            _services = await AppServices.CreateAsync(CancellationToken.None);
            _mainWindow = new MainWindow(_services);
            MainWindow = _mainWindow;
            _trayIconService = new TrayIconService(_mainWindow);
            _mainWindow.Show();
            _services.HotkeyListener.Start();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"启动失败：{ex.Message}",
                "本地语音输入",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _trayIconService?.Dispose();
        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        base.OnExit(e);
    }
}
