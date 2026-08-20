using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LocalAsrClient.App.Diagnostics;
using LocalAsrClient.App.TextInjection;
using LocalAsrClient.Core.Dictation;

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
    private readonly LoopingWaveformPreview _waveformPreview = new();
    private OverlayFocusSnapshot _focusSnapshotBeforeShow;
    private IntPtr _placementMonitor;
    private System.Windows.Threading.DispatcherTimer? _waveformPreviewTimer;
    private bool _isReviewMode;
    private bool _isPositioning;

    public DictationOverlayWindow()
        : this(NullDiagnosticEventSink.Instance)
    {
    }

    public DictationOverlayWindow(IDiagnosticEventSink diagnostics)
    {
        _diagnostics = diagnostics;
        _viewModel = new OverlayViewModel(OnCloseRequested, OnSubmitRequested, OnSegmentTextChanged);
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

    public event Action<Guid, string>? SegmentTextChanged;

    internal void LockPlacementToWindow(IntPtr targetWindow)
    {
        _placementMonitor = OverlayMonitorPlacement.CaptureMonitor(targetWindow);
    }

    public void SetRecordingLevel(float level)
    {
        if (!Dispatcher.CheckAccess())
        {
            if (!Dispatcher.HasShutdownStarted)
            {
                _ = Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Render,
                    () => SetRecordingLevel(level));
            }

            return;
        }

        if (!_viewModel.ShowRecordingLayout)
        {
            return;
        }

        RecordingWaveformView.PushLevel(level);
    }

    public void ShowRecordingPreview()
    {
        ShowOverlay(OverlayState.Recording, "聆听中");
        _waveformPreview.Reset();
        for (var index = 0; index < WaveformHistory.DefaultBarCount; index++)
        {
            RecordingWaveformView.PushLevel(_waveformPreview.NextLevel());
        }

        _waveformPreviewTimer = new System.Windows.Threading.DispatcherTimer(
            LoopingWaveformPreview.FrameInterval,
            System.Windows.Threading.DispatcherPriority.Render,
            OnWaveformPreviewTick,
            Dispatcher);
        _waveformPreviewTimer.Start();
    }

    public void ShowOverlay(OverlayState state, string message, string resultText = "", string? errorMessage = null)
    {
        _isReviewMode = false;
        StopWaveformPreview();
        _focusSnapshotBeforeShow = OverlayFocusGuard.Capture();
        EnsurePlacementMonitor(_focusSnapshotBeforeShow.ForegroundWindow);
        _ = _diagnostics.WriteAsync(CreateEvent("Overlay.Show.Before", state));

        RecordingWaveformView.Reset();
        _viewModel.ShowState(state, message, resultText, errorMessage);
        ApplyHeightConstraints();
        ShowWithoutActivation();
        UpdateLayout();
        PositionBottomCenterNoActivate();
        OverlayFocusGuard.RestoreIfChanged(_focusSnapshotBeforeShow, _interopHelper.Handle);

        _ = _diagnostics.WriteAsync(CreateEvent("Overlay.Show.After", state));
    }

    public void ApplyInPlaceStatus(InPlaceDictationStatus status)
    {
        StopWaveformPreview();
        var overlayState = ToOverlayState(status.State);
        _ = _diagnostics.WriteAsync(CreateEvent("Overlay.Show.Before", overlayState));
        var enteringReview = status.State == InPlaceDictationState.Reviewing && !_isReviewMode;
        _isReviewMode = status.State == InPlaceDictationState.Reviewing;
        if (!_isReviewMode)
        {
            _focusSnapshotBeforeShow = OverlayFocusGuard.Capture();
            EnsurePlacementMonitor(_focusSnapshotBeforeShow.ForegroundWindow);
        }

        _viewModel.ApplyInPlaceStatus(status);
        ApplyHeightConstraints();
        if (_isReviewMode)
        {
            ShowForReview(enteringReview);
        }
        else
        {
            ShowWithoutActivation();
        }

        UpdateLayout();
        PositionBottomCenterNoActivate();
        if (!_isReviewMode)
        {
            OverlayFocusGuard.RestoreIfChanged(_focusSnapshotBeforeShow, _interopHelper.Handle);
        }

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            () => InPlaceSegmentScrollViewer.ScrollToEnd());
        _ = _diagnostics.WriteAsync(CreateEvent("Overlay.Show.After", overlayState));
    }

    public void HideOverlay()
    {
        _isReviewMode = false;
        Focusable = false;
        ShowActivated = false;
        ConfigureNoActivateStyle(_interopHelper.Handle);
        StopWaveformPreview();
        RecordingWaveformView.Reset();
        var handle = _interopHelper.Handle;
        if (handle != IntPtr.Zero)
        {
            ShowWindow(handle, SwHide);
        }

        _placementMonitor = IntPtr.Zero;
    }

    private void OnCloseRequested()
    {
        CloseRequested?.Invoke();
    }

    private void OnSubmitRequested()
    {
        SubmitRequested?.Invoke();
    }

    private void OnSegmentTextChanged(Guid segmentId, string text)
    {
        SegmentTextChanged?.Invoke(segmentId, text);
    }

    private void OnWaveformPreviewTick(object? sender, EventArgs e)
    {
        RecordingWaveformView.PushLevel(_waveformPreview.NextLevel());
    }

    private void StopWaveformPreview()
    {
        if (_waveformPreviewTimer is null)
        {
            return;
        }

        _waveformPreviewTimer.Stop();
        _waveformPreviewTimer.Tick -= OnWaveformPreviewTick;
        _waveformPreviewTimer = null;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ConfigureNoActivateStyle(_interopHelper.Handle);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (!_isReviewMode)
        {
            OverlayFocusGuard.RestoreIfChanged(_focusSnapshotBeforeShow, _interopHelper.Handle);
        }
    }

    private void ShowWithoutActivation()
    {
        var handle = _interopHelper.Handle;
        Focusable = false;
        ShowActivated = false;
        ConfigureNoActivateStyle(handle);
        Visibility = Visibility.Visible;
        if (!IsVisible)
        {
            Show();
        }

        ShowWindow(handle, SwShownoactivate);
        EnsureTopmostNoActivate(handle);
    }

    private void ShowForReview(bool activate)
    {
        var handle = _interopHelper.Handle;
        ConfigureActivateStyle(handle);
        Focusable = true;
        ShowActivated = true;
        Visibility = Visibility.Visible;
        if (!IsVisible)
        {
            Show();
        }

        if (activate)
        {
            Activate();
        }
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

    private static void ConfigureActivateStyle(IntPtr handle)
    {
        var styles = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, (styles | WsExToolWindow) & ~WsExNoActivate);
    }

    private void ApplyHeightConstraints()
    {
        if (!_viewModel.ShowCopyLayout)
        {
            return;
        }

        var availableHeight = GetAvailableHeight();
        _viewModel.ResultMaxHeight = Math.Max(60, Math.Min(180, availableHeight - CopyLayoutChromeHeight));
    }

    private void PositionBottomCenterNoActivate()
    {
        if (_isPositioning)
        {
            return;
        }

        var handle = _interopHelper.Handle;
        if (handle == IntPtr.Zero || !TryGetWorkingArea(out var workingArea))
        {
            return;
        }

        _isPositioning = true;
        try
        {
            // The first move can change the window DPI. A second pass uses the
            // resulting native size and DPI so mixed-scale monitors stay centered.
            PositionOnWorkingArea(handle, workingArea);
            PositionOnWorkingArea(handle, workingArea);
        }
        finally
        {
            _isPositioning = false;
        }
    }

    private void PositionOnWorkingArea(IntPtr handle, OverlayPixelRectangle workingArea)
    {
        var dpi = OverlayMonitorPlacement.GetWindowDpiOrDefault(handle);
        var windowBounds = GetWindowBounds(handle, dpi);
        var position = OverlayPlacementCalculator.BottomCenter(
            workingArea,
            windowBounds.Width,
            windowBounds.Height,
            OverlayPlacementCalculator.DevicePixels(TopMargin, dpi),
            OverlayPlacementCalculator.DevicePixels(BottomMargin, dpi));

        Win32FocusNative.SetWindowPos(
            handle,
            Win32FocusNative.HwndTopmost,
            position.Left,
            position.Top,
            0,
            0,
            Win32FocusNative.SwpNoActivate | Win32FocusNative.SwpNosize | Win32FocusNative.SwpShowWindow);
    }

    private OverlayPixelRectangle GetWindowBounds(IntPtr handle, uint dpi)
    {
        if (OverlayMonitorPlacement.TryGetWindowBounds(handle, out var windowBounds))
        {
            return windowBounds;
        }

        var width = Math.Max(1, OverlayPlacementCalculator.DevicePixels(ActualWidth, dpi));
        var height = Math.Max(1, OverlayPlacementCalculator.DevicePixels(ActualHeight, dpi));
        return new OverlayPixelRectangle(0, 0, width, height);
    }

    private double GetAvailableHeight()
    {
        if (!TryGetWorkingArea(out var workingArea))
        {
            return SystemParameters.WorkArea.Height - BottomMargin - TopMargin;
        }

        var dpi = OverlayMonitorPlacement.GetWindowDpiOrDefault(_interopHelper.Handle);
        return workingArea.Height * (double)OverlayMonitorPlacement.DefaultDpi / dpi - BottomMargin - TopMargin;
    }

    private bool TryGetWorkingArea(out OverlayPixelRectangle workingArea)
    {
        var monitor = _placementMonitor;
        if (monitor == IntPtr.Zero)
        {
            monitor = OverlayMonitorPlacement.CaptureMonitor(_focusSnapshotBeforeShow.ForegroundWindow);
        }

        if (OverlayMonitorPlacement.TryGetWorkingArea(monitor, out workingArea))
        {
            return true;
        }

        monitor = OverlayMonitorPlacement.CaptureMonitor(IntPtr.Zero);
        return OverlayMonitorPlacement.TryGetWorkingArea(monitor, out workingArea);
    }

    private void EnsurePlacementMonitor(IntPtr targetWindow)
    {
        if (_placementMonitor == IntPtr.Zero)
        {
            _placementMonitor = OverlayMonitorPlacement.CaptureMonitor(targetWindow);
        }
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

    private static OverlayState ToOverlayState(InPlaceDictationState state) => state switch
    {
        InPlaceDictationState.EnsuringModelReady => OverlayState.LoadingModel,
        InPlaceDictationState.Recording => OverlayState.Recording,
        InPlaceDictationState.Reviewing => OverlayState.Reviewing,
        InPlaceDictationState.Finishing or InPlaceDictationState.Injecting => OverlayState.Transcribing,
        InPlaceDictationState.ResultNeedsAction => OverlayState.ResultNeedsAction,
        InPlaceDictationState.Error => OverlayState.Error,
        _ => OverlayState.Injected
    };

    private const int SwHide = 0;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
