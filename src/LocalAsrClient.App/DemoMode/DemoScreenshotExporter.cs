using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
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
            Path.Combine(outputDirectory, "services.png"),
            cancellationToken);
        await CaptureMainSectionAsync(
            services,
            MainSection.Services,
            Path.Combine(outputDirectory, "services-remote.png"),
            cancellationToken,
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
        double scrollOffset = 0)
    {
        var window = new MainWindow(services);
        window.Show();
        try
        {
            await window.ViewModel.Initialization;
            window.ViewModel.Navigation.SelectedSection = section;
            await WaitForLayoutAsync(window, cancellationToken);
            if (scrollOffset > 0)
            {
                var scrollViewer = FindVisibleScrollableViewer(window)
                    ?? throw new InvalidOperationException("没有找到可滚动的演示页面。");
                scrollViewer.ScrollToVerticalOffset(scrollOffset);
                await WaitForLayoutAsync(window, cancellationToken);
            }
            CaptureVisual(window, outputPath);
        }
        finally
        {
            window.AllowClose();
            window.Close();
        }
    }

    private static System.Windows.Controls.ScrollViewer? FindVisibleScrollableViewer(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is System.Windows.Controls.ScrollViewer scrollViewer
                && scrollViewer.IsVisible
                && scrollViewer.ScrollableHeight > 0)
            {
                return scrollViewer;
            }

            var nested = FindVisibleScrollableViewer(child);
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

    private static void CaptureVisual(Window window, string outputPath)
    {
        var windowHandle = new WindowInteropHelper(window).Handle;
        if (windowHandle == IntPtr.Zero || !GetWindowRect(windowHandle, out var rect))
        {
            throw new InvalidOperationException("无法获取演示窗口尺寸。");
        }

        var width = Math.Max(1, rect.Right - rect.Left);
        var height = Math.Max(1, rect.Bottom - rect.Top);
        using var bitmap = new System.Drawing.Bitmap(
            width,
            height,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(
            rect.Left,
            rect.Top,
            0,
            0,
            new System.Drawing.Size(width, height),
            System.Drawing.CopyPixelOperation.SourceCopy);

        var outputWidth = Math.Max(1, (int)Math.Round(window.Width));
        var outputHeight = Math.Max(1, (int)Math.Round(window.Height));
        using var output = new System.Drawing.Bitmap(
            outputWidth,
            outputHeight,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        output.SetResolution(96, 96);
        using (var outputGraphics = System.Drawing.Graphics.FromImage(output))
        {
            outputGraphics.CompositingQuality =
                System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            outputGraphics.InterpolationMode =
                System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            outputGraphics.PixelOffsetMode =
                System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            outputGraphics.DrawImage(
                bitmap,
                new System.Drawing.Rectangle(0, 0, outputWidth, outputHeight),
                new System.Drawing.Rectangle(0, 0, width, height),
                System.Drawing.GraphicsUnit.Pixel);
        }

        output.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out WindowRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
