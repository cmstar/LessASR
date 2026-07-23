using LocalAsrClient.App.Tray;

namespace LocalAsrClient.App.Tests.Tray;

public sealed class TrayWindowActivatorTests
{
    [Fact]
    public void RestoreAndActivate_RestoresShowsAndRequestsForeground()
    {
        var window = new FakeTrayWindow(isMinimized: true, handle: new IntPtr(42));
        var foreground = new FakeTrayForegroundService();
        var activator = new TrayWindowActivator(window, foreground);

        activator.RestoreAndActivate();

        Assert.False(window.IsMinimized);
        Assert.Equal(["Restore", "Show", "Activate"], window.Calls);
        Assert.Equal([new IntPtr(42)], foreground.RequestedHandles);
    }

    [Fact]
    public void RestoreAndActivate_DoesNotChangeAnAlreadyRestoredWindowState()
    {
        var window = new FakeTrayWindow(isMinimized: false, handle: new IntPtr(42));
        var foreground = new FakeTrayForegroundService();
        var activator = new TrayWindowActivator(window, foreground);

        activator.RestoreAndActivate();

        Assert.Equal(["Show", "Activate"], window.Calls);
        Assert.Equal([new IntPtr(42)], foreground.RequestedHandles);
    }

    private sealed class FakeTrayWindow(bool isMinimized, IntPtr handle) : ITrayWindow
    {
        public bool IsMinimized { get; private set; } = isMinimized;

        public IntPtr Handle => handle;

        public List<string> Calls { get; } = [];

        public void Restore()
        {
            Calls.Add("Restore");
            IsMinimized = false;
        }

        public void Show()
        {
            Calls.Add("Show");
        }

        public void Activate()
        {
            Calls.Add("Activate");
        }
    }

    private sealed class FakeTrayForegroundService : ITrayForegroundService
    {
        public List<IntPtr> RequestedHandles { get; } = [];

        public void RequestForeground(IntPtr handle)
        {
            RequestedHandles.Add(handle);
        }
    }
}
