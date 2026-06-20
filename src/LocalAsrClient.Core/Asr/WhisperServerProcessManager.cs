using System.Diagnostics;
using System.Text;
using LocalAsrClient.Core.Abstractions;

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
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly IAppLog? _log;
    private readonly object _outputLock = new();
    private readonly StringBuilder _processOutput = new();
    private WhisperServerOptions _options;
    private Process? _process;

    public WhisperServerProcessManager(WhisperServerOptions options, IAppLog? log = null)
    {
        _options = options;
        _log = log;
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
        await _startLock.WaitAsync(cancellationToken);
        try
        {
            if (_process is { HasExited: false } && Status == WhisperServerStatus.Ready)
            {
                return;
            }

            if (_process is { HasExited: false } && Status == WhisperServerStatus.Starting)
            {
                await WaitUntilReadyAsync(cancellationToken);
                Status = WhisperServerStatus.Ready;
                return;
            }

            ValidatePaths();

            if (await TryProbeAsync(cancellationToken))
            {
                Status = WhisperServerStatus.Ready;
                return;
            }

            StopManagedProcess();
            Status = WhisperServerStatus.Starting;
            var arguments = WhisperServerStartupArguments.Build(_options);
            ResetProcessOutput();
            _log?.Write("whisper-server 启动", WhisperServerLaunchDetails.FormatLaunch(arguments));

            var startInfo = new ProcessStartInfo
            {
                FileName = _options.ServerExecutablePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            _process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("无法启动 whisper-server。");

            AttachProcessOutputHandlers(_process);

            await WaitUntilReadyAsync(cancellationToken);
            Status = WhisperServerStatus.Ready;
        }
        finally
        {
            _startLock.Release();
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopManagedProcess();
        return Task.CompletedTask;
    }

    private void ValidatePaths()
    {
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
        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_process is { HasExited: true })
            {
                Status = WhisperServerStatus.Failed;
                throw CreateStartupFailureException(exitCode: _process.ExitCode);
            }

            if (await TryProbeAsync(cancellationToken))
            {
                return;
            }

            await Task.Delay(500, cancellationToken);
        }

        Status = WhisperServerStatus.Failed;
        throw CreateStartupFailureException(exitCode: null, timedOut: true);
    }

    private InvalidOperationException CreateStartupFailureException(int? exitCode, bool timedOut = false)
    {
        var processOutput = GetProcessOutputSnapshot();
        var failureDetails = WhisperServerLaunchDetails.FormatFailure(exitCode, processOutput, timedOut);

        _log?.Write("whisper-server 启动失败", failureDetails);

        return new InvalidOperationException(
            WhisperServerLaunchDetails.FormatFailureSummary(exitCode, processOutput, timedOut));
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

    private void ResetProcessOutput()
    {
        lock (_outputLock)
        {
            _processOutput.Clear();
        }
    }

    private void AttachProcessOutputHandlers(Process process)
    {
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                AppendProcessOutput(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                AppendProcessOutput(e.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    private void AppendProcessOutput(string line)
    {
        lock (_outputLock)
        {
            _processOutput.AppendLine(line);
        }
    }

    private string GetProcessOutputSnapshot()
    {
        lock (_outputLock)
        {
            return _processOutput.ToString();
        }
    }
}
