using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.ViewModels;
using LocalAsrClient.App.Views;
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
            Path.Combine(outputDirectory, "services.png"),
            cancellationToken,
            scrolledOutputPath: Path.Combine(outputDirectory, "services-remote.png"),
            scrollOffset: 560);
        await CaptureMainSectionAsync(
            services,
            MainSection.Settings,
            Path.Combine(outputDirectory, "settings.png"),
            cancellationToken);

        await PopulateContinuousDictationAsync(services, cancellationToken);
        var continuousWindow = services.ContinuousDictationCoordinator.CurrentWindow
            ?? throw new InvalidOperationException("连续听写演示窗口没有成功打开。");
        await WaitForLayoutAsync(continuousWindow, cancellationToken);
        CaptureVisual(
            continuousWindow,
            Path.Combine(outputDirectory, "continuous-dictation.png"));
    }

    private static async Task CaptureMainSectionAsync(
        AppServices services,
        MainSection section,
        string outputPath,
        CancellationToken cancellationToken,
        string? scrolledOutputPath = null,
        double scrollOffset = 0)
    {
        var window = new MainWindow(services);
        window.Show();
        try
        {
            await window.ViewModel.Initialization;
            window.ViewModel.Navigation.SelectedSection = section;
            await WaitForLayoutAsync(window, cancellationToken);
            var stableChrome = scrolledOutputPath is null
                ? null
                : CaptureNamedOverlays(window);
            CaptureVisual(window, outputPath);
            if (scrolledOutputPath is not null && scrollOffset > 0)
            {
                var serviceView = FindVisibleServiceView(window)
                    ?? throw new InvalidOperationException("没有找到服务页演示视图。");
                serviceView.ScrollToPageOffset(scrollOffset);
                await WaitForLayoutAsync(window, cancellationToken);
                CaptureVisual(window, scrolledOutputPath, stableChrome);
            }
        }
        finally
        {
            window.AllowClose();
            window.Close();
        }
    }

    private static ServiceView? FindVisibleServiceView(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is ServiceView serviceView && serviceView.IsVisible)
            {
                return serviceView;
            }

            var nested = FindVisibleServiceView(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
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

    private static void CaptureVisual(
        Window window,
        string outputPath,
        IReadOnlyList<CapturedOverlay>? capturedOverlays = null)
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
            drawingContext.DrawRectangle(
                CreateVisualBrush(visual),
                null,
                new Rect(0, 0, outputWidth, outputHeight));

            if (capturedOverlays is null)
            {
                DrawNamedOverlay(window, visual, drawingContext, "ScreenshotSidebar");
                DrawNamedOverlay(window, visual, drawingContext, "ScreenshotTitleBar");
            }
            else
            {
                foreach (var overlay in capturedOverlays)
                {
                    drawingContext.DrawImage(overlay.Image, overlay.Bounds);
                }
            }
        }

        bitmap.Render(background);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(outputPath);
        encoder.Save(output);
    }

    private static FrameworkElement GetCaptureRoot(Window window) =>
        window.FindName("ScreenshotRoot") as FrameworkElement
        ?? window.Content as FrameworkElement
        ?? throw new InvalidOperationException("演示窗口缺少可渲染的根内容。");

    private static IReadOnlyList<CapturedOverlay> CaptureNamedOverlays(Window window)
    {
        var captureRoot = GetCaptureRoot(window);
        var overlays = new List<CapturedOverlay>();
        foreach (var elementName in new[] { "ScreenshotSidebar", "ScreenshotTitleBar" })
        {
            if (window.FindName(elementName) is not FrameworkElement overlay
                || overlay.ActualWidth <= 0
                || overlay.ActualHeight <= 0)
            {
                continue;
            }

            var width = Math.Max(1, (int)Math.Round(overlay.ActualWidth));
            var height = Math.Max(1, (int)Math.Round(overlay.ActualHeight));
            var bitmap = new RenderTargetBitmap(
                width,
                height,
                96,
                96,
                PixelFormats.Pbgra32);
            var drawing = new DrawingVisual();
            using (var drawingContext = drawing.RenderOpen())
            {
                drawingContext.DrawRectangle(
                    CreateVisualBrush(overlay),
                    null,
                    new Rect(0, 0, width, height));
            }

            bitmap.Render(drawing);
            var bounds = overlay.TransformToAncestor(captureRoot).TransformBounds(
                new Rect(0, 0, overlay.ActualWidth, overlay.ActualHeight));
            overlays.Add(new CapturedOverlay(bitmap, bounds));
        }

        return overlays;
    }

    private static void DrawNamedOverlay(
        Window window,
        FrameworkElement captureRoot,
        DrawingContext drawingContext,
        string elementName)
    {
        if (window.FindName(elementName) is not FrameworkElement overlay
            || overlay.ActualWidth <= 0
            || overlay.ActualHeight <= 0)
        {
            return;
        }

        var bounds = overlay.TransformToAncestor(captureRoot).TransformBounds(
            new Rect(0, 0, overlay.ActualWidth, overlay.ActualHeight));
        drawingContext.DrawRectangle(
            CreateVisualBrush(overlay),
            null,
            bounds);
    }

    private static VisualBrush CreateVisualBrush(FrameworkElement visual) =>
        new(visual)
        {
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            Stretch = Stretch.None,
            Viewbox = new Rect(0, 0, visual.ActualWidth, visual.ActualHeight),
            ViewboxUnits = BrushMappingMode.Absolute,
        };

    private sealed record CapturedOverlay(BitmapSource Image, Rect Bounds);
}
