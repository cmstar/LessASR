using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.ViewModels;

namespace LocalAsrClient.App;

public partial class MainWindow : Window
{
    private readonly AppServices _services;
    private bool _allowClose;

    public MainWindow(AppServices services)
    {
        _services = services;
        InitializeComponent();
        DataContext = new MainViewModel(services);
    }

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

    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!SettingsTabItem.IsSelected || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        viewModel.Settings.ResetSaveFeedback();
    }
}
