using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LocalAsrClient.App.Overlay;

public partial class DictationOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const double BottomMargin = 20;
    private const double TopMargin = 16;
    private const double ChromeHeightWithoutResult = 130;
    private readonly OverlayViewModel _viewModel;

    public DictationOverlayWindow()
    {
        _viewModel = new OverlayViewModel(OnCloseRequested);
        InitializeComponent();
        DataContext = _viewModel;
        SizeChanged += (_, _) =>
        {
            if (IsVisible)
            {
                PositionBottomCenter();
            }
        };
    }

    public event Action? CloseRequested;

    public void ShowOverlay(OverlayState state, string message, string resultText = "", string? errorMessage = null)
    {
        _viewModel.ShowState(state, message, resultText, errorMessage);
        ApplyHeightConstraints();
        Show();
        UpdateLayout();
        PositionBottomCenter();
    }

    public void HideOverlay()
    {
        Hide();
    }

    private void OnCloseRequested()
    {
        HideOverlay();
        CloseRequested?.Invoke();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var styles = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, styles | WsExNoActivate);
    }

    private void ApplyHeightConstraints()
    {
        var area = SystemParameters.WorkArea;
        var availableHeight = area.Height - BottomMargin - TopMargin;
        var maxResultHeight = Math.Max(60, Math.Min(180, availableHeight - ChromeHeightWithoutResult));
        _viewModel.ResultMaxHeight = maxResultHeight;
    }

    private void PositionBottomCenter()
    {
        var area = SystemParameters.WorkArea;
        var availableHeight = area.Height - BottomMargin - TopMargin;

        if (ActualHeight > availableHeight)
        {
            _viewModel.ResultMaxHeight = Math.Max(
                60,
                availableHeight - ChromeHeightWithoutResult - (ActualHeight - availableHeight));
            UpdateLayout();
        }

        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Bottom - ActualHeight - BottomMargin;

        if (Top < area.Top + TopMargin)
        {
            Top = area.Top + TopMargin;
            _viewModel.ResultMaxHeight = Math.Max(60, area.Bottom - BottomMargin - Top - ChromeHeightWithoutResult);
            UpdateLayout();
            Top = area.Bottom - ActualHeight - BottomMargin;
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
