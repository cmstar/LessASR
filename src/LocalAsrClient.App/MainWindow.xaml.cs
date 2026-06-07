using System.ComponentModel;
using System.Windows;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.ViewModels;

namespace LocalAsrClient.App;

public partial class MainWindow : Window
{
    private bool _allowClose;

    public MainWindow(AppServices services)
    {
        InitializeComponent();
        DataContext = new MainViewModel(services);
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
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
