using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace LocalAsrClient.App.Tray;

public partial class TrayContextMenu : ContextMenu
{
    private readonly Action _showWindow;
    private readonly Action _exitApplication;

    public TrayContextMenu(Action showWindow, Action exitApplication)
    {
        _showWindow = showWindow;
        _exitApplication = exitApplication;
        InitializeComponent();
    }

    public void ShowAtPointer()
    {
        IsOpen = false;
        Placement = PlacementMode.MousePoint;
        IsOpen = true;
    }

    private void OnOpenWindowClick(object sender, RoutedEventArgs e)
    {
        IsOpen = false;
        _showWindow();
    }

    private void OnExitApplicationClick(object sender, RoutedEventArgs e)
    {
        IsOpen = false;
        _exitApplication();
    }
}
