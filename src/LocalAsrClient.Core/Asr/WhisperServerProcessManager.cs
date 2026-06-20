using System.Diagnostics;

namespace LocalAsrClient.Core.Asr;

public interface IWhisperServerManager
{
    WhisperServerStatus Status { get; }
    Uri BaseUri { get; }
    void UpdateOptions(WhisperServerOptions options);
    Task EnsureStartedAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task HealthCheckAsync(CancellationToken cancellationToken);
}

public sealed class WhisperServerProcessManager : IWhisperServerManager
{
    private WhisperServerOptions _options;
    private Process? _process;

    public WhisperServerProcessManager(WhisperServerOptions options)
    {
        _options = options;
    }

    public WhisperServerStatus Status { get; private set; } = WhisperServerStatus.Stopped;
    public Uri BaseUri => _options.BaseUri;

    public void UpdateOptions(WhisperServerOptions options)
    {
        var restartRequired = _options.Host != options.Host
            || _options.Port != options.Port
            || !string.Equals(_options.ServerExecutablePath, options.ServerExecutablePath, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_options.ModelPath, options.ModelPath, StringComparison.OrdinalIgnoreCase);

        _options = options;

        if (restartRequired)
        {
            StopManagedProcess();
        }
    }

    public async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false } && Status == WhisperServerStatus.Ready)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.ServerExecutablePath) || !File.Exists(_options.ServerExecutablePath))
        {
            Status = WhisperServerStatus.Failed;
            throw new FileNotFoundException("未找到 whisper-server 可执行文件。", _options.ServerExecutablePath);
        }

        if (string.IsNullOrWhiteSpace(_options.ModelPath) || !File.Exists(_options.ModelPath))
        {
            Status = WhisperServerStatus.Failed;
            throw new FileNotFoundException("未找到 Whisper 模型文件。", _options.ModelPath);
        }

        if (await TryProbeAsync(cancellationToken))
        {
            Status = WhisperServerStatus.Ready;
            return;
        }

        Status = WhisperServerStatus.Starting;
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ServerExecutablePath,
            Arguments = $"--host {_options.Host} --port {_options.Port} -m \"{_options.ModelPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 whisper-server。");

        await WaitUntilReadyAsync(cancellationToken);
        Status = WhisperServerStatus.Ready;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopManagedProcess();
        return Task.CompletedTask;
    }

    private void StopManagedProcess()
    {
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
            _process.Dispose();
        }

        _process = null;
        Status = WhisperServerStatus.Stopped;
    }

    public async Task HealthCheckAsync(CancellationToken cancellationToken)
    {
        if (!await TryProbeAsync(cancellationToken))
        {
            if (_process is null or { HasExited: true })
            {
                Status = WhisperServerStatus.Stopped;
            }

            throw new InvalidOperationException($"无法连接到 whisper-server：{_options.BaseUri}");
        }

        Status = WhisperServerStatus.Ready;
    }

    private async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_process is { HasExited: true })
            {
                Status = WhisperServerStatus.Failed;
                throw new InvalidOperationException("whisper-server 已退出。");
            }

            if (await TryProbeAsync(cancellationToken))
            {
                return;
            }

            await Task.Delay(500, cancellationToken);
        }

        Status = WhisperServerStatus.Failed;
        throw new TimeoutException("等待 whisper-server 启动超时。");
    }

    private async Task<bool> TryProbeAsync(CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient { BaseAddress = _options.BaseUri, Timeout = TimeSpan.FromSeconds(2) };

        try
        {
            using var response = await httpClient.GetAsync("/", cancellationToken);
            return (int)response.StatusCode < 500;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
