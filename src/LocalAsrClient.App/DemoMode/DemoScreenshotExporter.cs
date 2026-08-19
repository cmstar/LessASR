using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.ViewModels;
using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.App.DemoMode;

public static class DemoScreenshotExporter
{
    private static readonly TimeSpan SegmentTimeout = TimeSpan.FromSeconds(5);

    public static async Task ExportAsync(
        MainWindow mainWindow,
        AppServices services,
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        await mainWindow.ViewModel.Initialization;
        mainWindow.Hide();

        await CaptureMainSectionAsync(
            services,
            MainSection.Home,
            Path.Combine(outputDirectory, "home.png"),
            cancellationToken);
        await CaptureMainSectionAsync(
            services,
            MainSection.History,
            Path.Combine(outputDirectory, "history.png"),
            cancellationToken);
        await CaptureMainSectionAsync(
            services,
            MainSection.Services,
            Path.Combine(outputDirectory, "services-remote.png"),
            cancellationToken,
            selectRemoteModel: true);
        await CaptureMainSectionAsync(
            services,
            MainSection.Vocabulary,
            Path.Combine(outputDirectory, "vocabulary.png"),
            cancellationToken);

        await PopulateInPlaceDictationAsync(services, cancellationToken);
        await WaitForLayoutAsync(services.OverlayWindow, cancellationToken);
        CaptureVisual(
            services.OverlayWindow,
            Path.Combine(outputDirectory, "in-place-dictation.png"));
        await services.InPlaceOrchestrator.CancelOrDismissAsync(cancellationToken);
        await services.InPlaceOrchestrator.CancelOrDismissAsync(cancellationToken);

        await PopulateContinuousDictationAsync(services, cancellationToken);
        var continuousWindow = services.ContinuousDictationCoordinator.CurrentWindow
            ?? throw new InvalidOperationException("独立听写演示窗口没有成功打开。");
        await WaitForLayoutAsync(continuousWindow, cancellationToken);
        CaptureVisual(
            continuousWindow,
            Path.Combine(outputDirectory, "independent-dictation.png"));
    }

    private static async Task PopulateInPlaceDictationAsync(
        AppServices services,
        CancellationToken cancellationToken)
    {
        var session = services.InPlaceDictationSession;
        await services.InPlaceOrchestrator.ToggleAsync(cancellationToken);
        for (var targetCompleted = 1; targetCompleted <= 2; targetCompleted++)
        {
            await WaitForSnapshotAfterAsync(
                session,
                () => services.InPlaceOrchestrator.CommitSegmentBoundaryAsync(cancellationToken),
                snapshot => snapshot.CompletedCount >= targetCompleted,
                cancellationToken);
        }
    }

    private static async Task CaptureMainSectionAsync(
        AppServices services,
        MainSection section,
        string outputPath,
        CancellationToken cancellationToken,
        bool selectRemoteModel = false)
    {
        var window = new MainWindow(services);
        window.Show();
        try
        {
            await window.ViewModel.Initialization;
            if (selectRemoteModel)
            {
                var remoteModel = window.ViewModel.Services.ModelProviders
                    .FirstOrDefault(model => !model.IsLocal)
                    ?? throw new InvalidOperationException("没有找到远程模型演示配置。");
                window.ViewModel.Services.SelectModelProvider(remoteModel);
            }

            window.ViewModel.Navigation.SelectedSection = section;
            await WaitForLayoutAsync(window, cancellationToken);
            CaptureWindow(window, outputPath);
        }
        finally
        {
            window.AllowClose();
            window.Close();
        }
    }

    private static void CaptureWindow(Window window, string outputPath)
    {
        var visual = GetCaptureRoot(window);
        var dpi = VisualTreeHelper.GetDpi(visual);
        var outputWidth = Math.Max(1, (int)Math.Round(visual.ActualWidth));
        var outputHeight = Math.Max(1, (int)Math.Round(visual.ActualHeight));
        var sourceWidth = Math.Max(1, (int)Math.Round(visual.ActualWidth * dpi.DpiScaleX));
        var sourceHeight = Math.Max(1, (int)Math.Round(visual.ActualHeight * dpi.DpiScaleY));

        using var sourceBitmap = CaptureWindowFrame(window, sourceWidth, sourceHeight);

        if (sourceWidth == outputWidth && sourceHeight == outputHeight)
        {
            sourceBitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
            return;
        }

        using var outputBitmap = new System.Drawing.Bitmap(
            outputWidth,
            outputHeight,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = System.Drawing.Graphics.FromImage(outputBitmap))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            graphics.DrawImage(
                sourceBitmap,
                new System.Drawing.Rectangle(0, 0, outputWidth, outputHeight));
        }

        outputBitmap.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static System.Drawing.Bitmap CaptureWindowFrame(
        Window window,
        int width,
        int height)
    {
        var bitmap = new System.Drawing.Bitmap(
            width,
            height,
            System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        var deviceContext = graphics.GetHdc();
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            const uint renderFullContent = 0x2;
            if (!PrintWindow(handle, deviceContext, renderFullContent))
            {
                bitmap.Dispose();
                throw new Win32Exception(
                    Marshal.GetLastWin32Error(),
                    "无法捕获 LessASR 演示窗口。");
            }
        }
        finally
        {
            graphics.ReleaseHdc(deviceContext);
        }

        return bitmap;
    }

    private static async Task PopulateContinuousDictationAsync(
        AppServices services,
        CancellationToken cancellationToken)
    {
        var coordinator = services.ContinuousDictationCoordinator;
        var session = services.ContinuousDictationSession;
        await coordinator.ShowAndToggleRecordingAsync();

        for (var targetCompleted = 1;
             targetCompleted < DemoDataScenario.ContinuousDictationSegments.Count;
             targetCompleted++)
        {
            await WaitForSnapshotAfterAsync(
                session,
                coordinator.CommitSegmentBoundaryAsync,
                snapshot => snapshot.CompletedCount >= targetCompleted,
                cancellationToken);
        }

        await WaitForSnapshotAfterAsync(
            session,
            coordinator.ShowAndToggleRecordingAsync,
            snapshot => snapshot.CompletedCount >= DemoDataScenario.ContinuousDictationSegments.Count
                        && !snapshot.IsRecordingActive,
            cancellationToken);
    }

    private static async Task WaitForSnapshotAfterAsync(
        ContinuousDictationSession session,
        Func<Task> action,
        Func<ContinuousDictationSnapshot, bool> predicate,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void OnChanged(ContinuousDictationSnapshot snapshot)
        {
            if (predicate(snapshot))
            {
                completion.TrySetResult();
            }
        }

        session.Changed += OnChanged;
        try
        {
            await action();
            await completion.Task.WaitAsync(SegmentTimeout, cancellationToken);
        }
        finally
        {
            session.Changed -= OnChanged;
        }
    }

    private static async Task WaitForLayoutAsync(
        Window window,
        CancellationToken cancellationToken)
    {
        window.Topmost = true;
        window.Show();
        window.Activate();
        if (!window.IsLoaded)
        {
            var loaded = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void OnLoaded(object sender, RoutedEventArgs args) => loaded.TrySetResult();

            window.Loaded += OnLoaded;
            try
            {
                if (!window.IsLoaded)
                {
                    await loaded.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
            finally
            {
                window.Loaded -= OnLoaded;
            }
        }

        await window.Dispatcher.InvokeAsync(
            window.UpdateLayout,
            DispatcherPriority.ApplicationIdle,
            cancellationToken);
        await Task.Delay(300, cancellationToken);
        await window.Dispatcher.InvokeAsync(
            window.UpdateLayout,
            DispatcherPriority.ApplicationIdle,
            cancellationToken);
    }

    private static void CaptureVisual(Window window, string outputPath)
    {
        var visual = GetCaptureRoot(window);
        var outputWidth = Math.Max(1, (int)Math.Round(visual.ActualWidth));
        var outputHeight = Math.Max(1, (int)Math.Round(visual.ActualHeight));
        var bitmap = new RenderTargetBitmap(
            outputWidth,
            outputHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawingContext = background.RenderOpen())
        {
            drawingContext.DrawRectangle(
                window.Background,
                null,
                new Rect(0, 0, outputWidth, outputHeight));
        }

        bitmap.Render(background);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(outputPath);
        encoder.Save(output);
    }

    private static FrameworkElement GetCaptureRoot(Window window) =>
        window.FindName("ScreenshotRoot") as FrameworkElement
        ?? window.Content as FrameworkElement
        ?? throw new InvalidOperationException("演示窗口缺少可渲染的根内容。");

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(
        IntPtr windowHandle,
        IntPtr targetDeviceContext,
        uint flags);

}
