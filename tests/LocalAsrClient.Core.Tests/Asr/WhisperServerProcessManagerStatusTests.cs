using LocalAsrClient.Core.Asr;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace LocalAsrClient.Core.Tests.Asr;

public sealed class WhisperServerProcessManagerStatusTests
{
    [Fact]
    public async Task EnsureStartedAsync_WhenConfigurationIsInvalid_PublishesFailedStatus()
    {
        var manager = new WhisperServerProcessManager(new WhisperServerOptions(
            "missing-whisper-server.exe",
            "missing-model.bin",
            "127.0.0.1",
            18080));
        var publishedStatuses = new List<WhisperServerStatus>();
        manager.StatusChanged += publishedStatuses.Add;

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            manager.EnsureStartedAsync(CancellationToken.None));

        Assert.Equal(WhisperServerStatus.Failed, manager.Status);
        Assert.Equal([WhisperServerStatus.Failed], publishedStatuses);
    }

    [Fact]
    public async Task UpdateOptions_WhileReady_MarksRestartRequiredWithoutInterruptingActiveEndpoint()
    {
        var executable = Path.GetTempFileName();
        var model = Path.GetTempFileName();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var responseTask = RespondOkOnceAsync(listener);
        try
        {
            var manager = new WhisperServerProcessManager(new WhisperServerOptions(
                executable, model, "127.0.0.1", port));
            await manager.EnsureStartedAsync(CancellationToken.None);

            manager.UpdateOptions(new WhisperServerOptions(
                executable, model, "127.0.0.1", port + 1));

            Assert.Equal(WhisperServerStatus.Ready, manager.Status);
            Assert.Equal(port, manager.BaseUri.Port);
            Assert.True(manager.IsRestartRequired);

            await manager.StopAsync(CancellationToken.None);

            Assert.Equal(port + 1, manager.BaseUri.Port);
            Assert.False(manager.IsRestartRequired);
            await responseTask;
        }
        finally
        {
            File.Delete(executable);
            File.Delete(model);
        }
    }

    [Fact]
    public async Task StopAsync_CancelsAnInProgressStartBeforeAProcessCanBeLaunched()
    {
        var executable = Path.GetTempFileName();
        var model = Path.GetTempFileName();
        var probe = new BlockingProbe();
        var processFactory = new FakeProcessFactory();
        var manager = new WhisperServerProcessManager(
            new WhisperServerOptions(executable, model, "127.0.0.1", 18080),
            log: null,
            probe,
            processFactory);
        try
        {
            var startTask = manager.EnsureStartedAsync(CancellationToken.None);
            await probe.Entered.WaitAsync(TimeSpan.FromSeconds(2));

            await manager.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startTask);
            Assert.Equal(0, processFactory.StartCalls);
            Assert.Equal(WhisperServerStatus.Stopped, manager.Status);
        }
        finally
        {
            File.Delete(executable);
            File.Delete(model);
        }
    }

    [Fact]
    public async Task StopAsync_WaitsForKilledProcessToExitBeforeReportingStopped()
    {
        var executable = Path.GetTempFileName();
        var model = Path.GetTempFileName();
        var process = new FakeProcess();
        var manager = new WhisperServerProcessManager(
            new WhisperServerOptions(executable, model, "127.0.0.1", 18080),
            log: null,
            new SequenceProbe(false, true),
            new FakeProcessFactory(process));
        try
        {
            await manager.EnsureStartedAsync(CancellationToken.None);

            var stopTask = manager.StopAsync(CancellationToken.None);
            await process.KillObserved.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.False(stopTask.IsCompleted);
            Assert.Equal(WhisperServerStatus.Ready, manager.Status);

            process.CompleteExit();
            await stopTask.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(process.WaitForExitCalled);
            Assert.True(process.Disposed);
            Assert.Equal(WhisperServerStatus.Stopped, manager.Status);
        }
        finally
        {
            File.Delete(executable);
            File.Delete(model);
        }
    }

    [Fact]
    public async Task StopAsync_DuringPostLaunchReadiness_DoesNotReportStoppedBeforeProcessExits()
    {
        var executable = Path.GetTempFileName();
        var model = Path.GetTempFileName();
        var process = new FakeProcess();
        var probe = new HealthyAfterLaunchBlockingProbe();
        var manager = new WhisperServerProcessManager(
            new WhisperServerOptions(executable, model, "127.0.0.1", 18080),
            log: null,
            probe,
            new FakeProcessFactory(process));
        try
        {
            var startTask = manager.EnsureStartedAsync(CancellationToken.None);
            await probe.PostLaunchProbeEntered.WaitAsync(TimeSpan.FromSeconds(2));

            var stopTask = manager.StopAsync(CancellationToken.None);
            await process.KillObserved.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.False(stopTask.IsCompleted);
            Assert.Equal(WhisperServerStatus.Starting, manager.Status);

            process.CompleteExit();
            await stopTask.WaitAsync(TimeSpan.FromSeconds(2));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => startTask);
            Assert.Equal(WhisperServerStatus.Stopped, manager.Status);
        }
        finally
        {
            File.Delete(executable);
            File.Delete(model);
        }
    }

    [Fact]
    public async Task StopAsync_WhenWaitingForExitFails_RetainsProcessForAnotherStopAttempt()
    {
        var executable = Path.GetTempFileName();
        var model = Path.GetTempFileName();
        var process = new FakeProcess
        {
            WaitForExitException = new IOException("wait failed")
        };
        var manager = new WhisperServerProcessManager(
            new WhisperServerOptions(executable, model, "127.0.0.1", 18080),
            log: null,
            new SequenceProbe(false, true),
            new FakeProcessFactory(process));
        try
        {
            await manager.EnsureStartedAsync(CancellationToken.None);

            await Assert.ThrowsAsync<IOException>(() => manager.StopAsync(CancellationToken.None));
            Assert.False(process.Disposed);

            process.WaitForExitException = null;
            process.ResetExitCompletion();
            var secondStop = manager.StopAsync(CancellationToken.None);
            process.CompleteExit();
            await secondStop.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(2, process.KillCalls);
            Assert.True(process.Disposed);
            Assert.Equal(WhisperServerStatus.Stopped, manager.Status);
        }
        finally
        {
            File.Delete(executable);
            File.Delete(model);
        }
    }

    private static async Task RespondOkOnceAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var buffer = new byte[1024];
        _ = await stream.ReadAsync(buffer);
        var response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response);
    }

    private sealed class BlockingProbe : IWhisperServerHealthProbe
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;

        public async Task<bool> IsHealthyAsync(Uri baseUri, CancellationToken cancellationToken)
        {
            _entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return false;
        }
    }

    private sealed class SequenceProbe(params bool[] results) : IWhisperServerHealthProbe
    {
        private readonly Queue<bool> _results = new(results);

        public Task<bool> IsHealthyAsync(Uri baseUri, CancellationToken cancellationToken) =>
            Task.FromResult(_results.Count > 0 && _results.Dequeue());
    }

    private sealed class HealthyAfterLaunchBlockingProbe : IWhisperServerHealthProbe
    {
        private int _callCount;
        private readonly TaskCompletionSource _postLaunchProbeEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task PostLaunchProbeEntered => _postLaunchProbeEntered.Task;

        public async Task<bool> IsHealthyAsync(Uri baseUri, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                return false;
            }

            _postLaunchProbeEntered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return false;
        }
    }

    private sealed class FakeProcessFactory : IWhisperServerProcessFactory
    {
        private readonly IWhisperServerProcess? _process;

        public FakeProcessFactory(IWhisperServerProcess? process = null)
        {
            _process = process;
        }

        public int StartCalls { get; private set; }

        public IWhisperServerProcess Start(ProcessStartInfo startInfo)
        {
            StartCalls++;
            return _process ?? throw new InvalidOperationException("Process creation was not expected.");
        }
    }

    private sealed class FakeProcess : IWhisperServerProcess
    {
        private TaskCompletionSource _exitCompletion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public event Action<IWhisperServerProcess>? Exited
        {
            add { }
            remove { }
        }

        public event Action<string>? OutputReceived
        {
            add { }
            remove { }
        }

        public event Action<string>? ErrorReceived
        {
            add { }
            remove { }
        }

        public bool HasExited { get; private set; }
        public int ExitCode => 0;
        public int KillCalls { get; private set; }
        public bool WaitForExitCalled { get; private set; }
        public bool Disposed { get; private set; }
        public Exception? WaitForExitException { get; set; }
        public Task KillObserved => _killObserved.Task;

        private readonly TaskCompletionSource _killObserved =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void SuppressExitNotification()
        {
        }

        public void Kill()
        {
            KillCalls++;
            _killObserved.TrySetResult();
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            WaitForExitCalled = true;
            return WaitForExitException is null
                ? _exitCompletion.Task.WaitAsync(cancellationToken)
                : Task.FromException(WaitForExitException);
        }

        public void BeginOutputRead()
        {
        }

        public void CompleteExit()
        {
            HasExited = true;
            _exitCompletion.TrySetResult();
        }

        public void ResetExitCompletion()
        {
            HasExited = false;
            _exitCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
