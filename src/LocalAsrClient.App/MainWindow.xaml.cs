using System.ComponentModel;
using System.Windows;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.ViewModels;
using Wpf.Ui.Controls;

namespace LocalAsrClient.App;

public partial class MainWindow : FluentWindow
{
    private readonly AppServices _services;
    private bool _allowClose;

    public MainWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        ViewModel = new MainViewModel(services);
        DataContext = ViewModel;
    }

    public MainViewModel ViewModel { get; }

    public void AllowClose()
    {
        _allowClose = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_allowClose)
        {
            base.OnClosing(e);
            return;
        }

        var minimizeToTray = _services.SettingsStore
            .LoadAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .MinimizeToTrayOnClose;

        if (minimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _allowClose = true;
        base.OnClosing(e);
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            WindowState = WindowState.Normal;
        }
    }

}
