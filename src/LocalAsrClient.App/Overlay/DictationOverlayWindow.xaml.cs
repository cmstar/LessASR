using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LocalAsrClient.App.Diagnostics;
using LocalAsrClient.App.TextInjection;

namespace LocalAsrClient.App.Overlay;

public partial class DictationOverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int SwShownoactivate = 4;
    private const double BottomMargin = 15;
    private const double TopMargin = 16;
    private const double CopyLayoutChromeHeight = 148;
    private readonly IDiagnosticEventSink _diagnostics;
    private readonly OverlayViewModel _viewModel;
    private readonly WindowInteropHelper _interopHelper;
    private OverlayFocusSnapshot _focusSnapshotBeforeShow;

    public DictationOverlayWindow()
        : this(NullDiagnosticEventSink.Instance)
    {
    }

    public DictationOverlayWindow(IDiagnosticEventSink diagnostics)
    {
        _diagnostics = diagnostics;
        _viewModel = new OverlayViewModel(OnCloseRequested, OnSubmitRequested);
        InitializeComponent();
        DataContext = _viewModel;
        _interopHelper = new WindowInteropHelper(this);
        ConfigureNoActivateStyle(_interopHelper.EnsureHandle());
        PrimeLayoutWithoutActivation();
        SizeChanged += (_, _) =>
        {
            PositionBottomCenterNoActivate();
        };
    }

    public event Action? CloseRequested;

    public event Action? SubmitRequested;

    public void ShowOverlay(OverlayState state, string message, string resultText = "", string? errorMessage = null)
    {
        _focusSnapshotBeforeShow = OverlayFocusGuard.Capture();
        _ = _diagnostics.WriteAsync(CreateEvent("Overlay.Show.Before", state));

        _viewModel.ShowState(state, message, resultText, errorMessage);
        ApplyHeightConstraints();
        ShowWithoutActivation();
        UpdateLayout();
        PositionBottomCenterNoActivate();
        OverlayFocusGuard.RestoreIfChanged(_focusSnapshotBeforeShow, _interopHelper.Handle);

        _ = _diagnostics.WriteAsync(CreateEvent("Overlay.Show.After", state));
    }

    public void HideOverlay()
    {
        var handle = _interopHelper.Handle;
        if (handle != IntPtr.Zero)
        {
            ShowWindow(handle, SwHide);
        }
    }

    private void OnCloseRequested()
    {
        HideOverlay();
        CloseRequested?.Invoke();
    }

    private void OnSubmitRequested()
    {
        SubmitRequested?.Invoke();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ConfigureNoActivateStyle(_interopHelper.Handle);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        OverlayFocusGuard.RestoreIfChanged(_focusSnapshotBeforeShow, _interopHelper.Handle);
    }

    private void ShowWithoutActivation()
    {
        var handle = _interopHelper.Handle;
        ConfigureNoActivateStyle(handle);
        Visibility = Visibility.Visible;
        if (!IsVisible)
        {
            Show();
        }

        ShowWindow(handle, SwShownoactivate);
        EnsureTopmostNoActivate(handle);
    }

    private void PrimeLayoutWithoutActivation()
    {
        var snapshot = OverlayFocusGuard.Capture();
        Visibility = Visibility.Visible;
        Show();
        UpdateLayout();
        ShowWindow(_interopHelper.Handle, SwHide);
        OverlayFocusGuard.RestoreIfChanged(snapshot, _interopHelper.Handle);
    }

    private static void ConfigureNoActivateStyle(IntPtr handle)
    {
        var styles = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, styles | WsExNoActivate | WsExToolWindow);
    }

    private void ApplyHeightConstraints()
    {
        if (!_viewModel.ShowCopyLayout)
        {
            return;
        }

        var area = SystemParameters.WorkArea;
        var availableHeight = area.Height - BottomMargin - TopMargin;
        _viewModel.ResultMaxHeight = Math.Max(60, Math.Min(180, availableHeight - CopyLayoutChromeHeight));
    }

    private void PositionBottomCenterNoActivate()
    {
        var area = SystemParameters.WorkArea;

        Left = area.Left + (area.Width - Width) / 2;
        Top = area.Bottom - ActualHeight - BottomMargin;

        if (Top < area.Top + TopMargin)
        {
            Top = area.Top + TopMargin;
        }

        var handle = _interopHelper.Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
        {
            Win32FocusNative.SetWindowPos(
                handle,
                Win32FocusNative.HwndTopmost,
                0,
                0,
                0,
                0,
                Win32FocusNative.SwpNoActivate | Win32FocusNative.SwpNomove | Win32FocusNative.SwpNosize | Win32FocusNative.SwpShowWindow);
            return;
        }

        var transform = source.CompositionTarget.TransformToDevice;
        var physicalLeft = (int)(Left * transform.M11);
        var physicalTop = (int)(Top * transform.M22);
        var physicalWidth = Math.Max(1, (int)(ActualWidth * transform.M11));
        var physicalHeight = Math.Max(1, (int)(ActualHeight * transform.M22));
        Win32FocusNative.SetWindowPos(
            handle,
            Win32FocusNative.HwndTopmost,
            physicalLeft,
            physicalTop,
            physicalWidth,
            physicalHeight,
            Win32FocusNative.SwpNoActivate | Win32FocusNative.SwpShowWindow);
    }

    private static void EnsureTopmostNoActivate(IntPtr handle)
    {
        Win32FocusNative.SetWindowPos(
            handle,
            Win32FocusNative.HwndTopmost,
            0,
            0,
            0,
            0,
            Win32FocusNative.SwpNoActivate | Win32FocusNative.SwpNomove | Win32FocusNative.SwpNosize | Win32FocusNative.SwpShowWindow);
    }

    private DiagnosticEvent CreateEvent(string eventName, OverlayState state)
    {
        return new DiagnosticEvent(
            0,
            DateTimeOffset.Now,
            eventName,
            state.ToString(),
            Environment.CurrentManagedThreadId,
            DiagnosticSnapshotCollector.Capture(),
            new Dictionary<string, string?>());
    }

    private const int SwHide = 0;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
