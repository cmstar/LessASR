using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LocalAsrClient.App.Overlay;

public partial class DictationOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private readonly OverlayViewModel _viewModel = new();

    public DictationOverlayWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    public void ShowOverlay(OverlayState state, string message, string resultText = "")
    {
        _viewModel.ShowState(state, message, resultText);
        PositionBottomCenter();
        Show();
    }

    public void HideOverlay()
    {
        Hide();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var styles = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, styles | WsExNoActivate);
    }

    private void PositionBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Bottom - ActualHeight - 80;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
