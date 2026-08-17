using System.Windows;
using System.Windows.Threading;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.DemoMode;
using LocalAsrClient.App.Infrastructure;
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
            var startupOptions = AppStartupOptions.Resolve(e.Args);
            AppExceptionLogger.ConfigureLogsDirectory(startupOptions.Paths.LogsDirectory);
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            _services = await AppServices.CreateAsync(startupOptions, CancellationToken.None);
            _mainWindow = new MainWindow(_services);
            MainWindow = _mainWindow;
            _trayIconService = new TrayIconService(_mainWindow);
            _mainWindow.Show();

            if (startupOptions.DemoScreenshotOutputDirectory is not null)
            {
                await DemoScreenshotExporter.ExportAsync(
                    _mainWindow,
                    _services,
                    startupOptions.DemoScreenshotOutputDirectory,
                    CancellationToken.None);
                _mainWindow.AllowClose();
                Shutdown();
                return;
            }

            _services.ContinuousDictationHotkeyListener.Start();
            // Start the modifier listener last so it observes any dedicated hotkey
            // messages before another low-level hook suppresses a solo key press.
            _services.HotkeyListener.Start();
            _services.EscapeCancelListener.Start();
        }
        catch (Exception ex)
        {
            AppExceptionLogger.Report(ex, "应用启动失败", showDialog: true);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppExceptionLogger.Report(e.Exception, "UI 线程未处理异常");
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            AppExceptionLogger.Report(exception, "进程未处理异常", isTerminating: e.IsTerminating);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppExceptionLogger.Report(e.Exception, "未观察到的 Task 异常", showDialog: false);
        e.SetObserved();
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
