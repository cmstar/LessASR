using System.Diagnostics;
using System.Text;
using LocalAsrClient.Core.Abstractions;

namespace LocalAsrClient.Core.Asr;

public interface IWhisperServerManager
{
    event Action<WhisperServerStatus>? StatusChanged;
    WhisperServerStatus Status { get; }
    Uri BaseUri { get; }
    bool IsRestartRequired => false;
    string ActiveModelPath => string.Empty;
    void UpdateOptions(WhisperServerOptions options);
    Task EnsureStartedAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
    Task HealthCheckAsync(CancellationToken cancellationToken);
}

public interface IWhisperServerHealthProbe
{
    Task<bool> IsHealthyAsync(Uri baseUri, CancellationToken cancellationToken);
}

public interface IWhisperServerProcess : IDisposable
{
    event Action<IWhisperServerProcess>? Exited;
    event Action<string>? OutputReceived;
    event Action<string>? ErrorReceived;

    bool HasExited { get; }
    int ExitCode { get; }
    void SuppressExitNotification();
    void Kill();
    Task WaitForExitAsync(CancellationToken cancellationToken);
    void BeginOutputRead();
}

public interface IWhisperServerProcessFactory
{
    IWhisperServerProcess Start(ProcessStartInfo startInfo);
}

public sealed class WhisperServerProcessManager : IWhisperServerManager
{
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private readonly object _lifecycleLock = new();
    private readonly IAppLog? _log;
    private readonly IWhisperServerHealthProbe _healthProbe;
    private readonly IWhisperServerProcessFactory _processFactory;
    private readonly object _outputLock = new();
    private readonly StringBuilder _processOutput = new();
    private WhisperServerOptions _options;
    private WhisperServerOptions _configuredOptions;
    private IWhisperServerProcess? _process;
    private CancellationTokenSource? _startupCancellation;
    private int _pendingStopCount;

    public WhisperServerProcessManager(WhisperServerOptions options, IAppLog? log = null)
        : this(
            options,
            log,
            new HttpWhisperServerHealthProbe(),
            new SystemWhisperServerProcessFactory())
    {
    }

    public WhisperServerProcessManager(
        WhisperServerOptions options,
        IAppLog? log,
        IWhisperServerHealthProbe healthProbe,
        IWhisperServerProcessFactory processFactory)
    {
        _options = options;
        _configuredOptions = options;
        _log = log;
        _healthProbe = healthProbe;
        _processFactory = processFactory;
    }

    public event Action<WhisperServerStatus>? StatusChanged;

    public WhisperServerStatus Status { get; private set; } = WhisperServerStatus.Stopped;
    public Uri BaseUri => _options.BaseUri;
    public bool IsRestartRequired { get; private set; }
    public string ActiveModelPath => _options.ModelPath;

    public void UpdateOptions(WhisperServerOptions options)
    {
        _configuredOptions = options;
        if (Status is WhisperServerStatus.Stopped or WhisperServerStatus.Failed
            && _process is null or { HasExited: true })
        {
            ApplyConfiguredOptions();
            return;
        }

        IsRestartRequired = OptionsDiffer(_options, _configuredOptions);
    }

    public async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        await _startLock.WaitAsync(cancellationToken);
        CancellationTokenSource? startupCancellation = null;
        try
        {
            lock (_lifecycleLock)
            {
                if (_pendingStopCount > 0)
                {
                    throw new OperationCanceledException("whisper-server 启动已被停止操作取消。");
                }

                startupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _startupCancellation = startupCancellation;
            }

            var startupToken = startupCancellation.Token;
            if (_process is { HasExited: false } && Status == WhisperServerStatus.Ready)
            {
                return;
            }

            if (_process is { HasExited: false } && Status == WhisperServerStatus.Starting)
            {
                await WaitUntilReadyAsync(startupToken);
                SetStatus(WhisperServerStatus.Ready);
                return;
            }

            if (Status is WhisperServerStatus.Stopped or WhisperServerStatus.Failed)
            {
                ApplyConfiguredOptions();
            }

            ValidatePaths();

            if (await TryProbeAsync(startupToken))
            {
                SetStatus(WhisperServerStatus.Ready);
                return;
            }

            await StopManagedProcessAsync(startupToken);
            SetStatus(WhisperServerStatus.Starting);
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

            _process = _processFactory.Start(startInfo);
            _process.Exited += OnManagedProcessExited;
            AttachProcessOutputHandlers(_process);

            await WaitUntilReadyAsync(startupToken);
            SetStatus(WhisperServerStatus.Ready);
        }
        catch (OperationCanceledException) when (IsStopRequested())
        {
            throw;
        }
        catch
        {
            SetStatus(WhisperServerStatus.Failed);
            throw;
        }
        finally
        {
            lock (_lifecycleLock)
            {
                if (ReferenceEquals(_startupCancellation, startupCancellation))
                {
                    _startupCancellation = null;
                }
            }

            startupCancellation?.Dispose();
            _startLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? startupCancellation;
        lock (_lifecycleLock)
        {
            _pendingStopCount++;
            startupCancellation = _startupCancellation;
        }

        startupCancellation?.Cancel();

        var lockTaken = false;
        try
        {
            await _startLock.WaitAsync(cancellationToken);
            lockTaken = true;
            await StopManagedProcessAsync(cancellationToken);
            ApplyConfiguredOptions();
        }
        finally
        {
            lock (_lifecycleLock)
            {
                _pendingStopCount--;
            }

            if (lockTaken)
            {
                _startLock.Release();
            }
        }
    }

    private void ApplyConfiguredOptions()
    {
        _options = _configuredOptions;
        IsRestartRequired = false;
    }

    private static bool OptionsDiffer(WhisperServerOptions left, WhisperServerOptions right) =>
        left.Host != right.Host
        || left.Port != right.Port
        || left.ThreadCount != right.ThreadCount
        || !string.Equals(left.ServerExecutablePath, right.ServerExecutablePath, StringComparison.OrdinalIgnoreCase)
        || !string.Equals(left.ModelPath, right.ModelPath, StringComparison.OrdinalIgnoreCase);

    private void ValidatePaths()
    {
        if (string.IsNullOrWhiteSpace(_options.ServerExecutablePath) || !File.Exists(_options.ServerExecutablePath))
        {
            SetStatus(WhisperServerStatus.Failed);
            throw new FileNotFoundException("未找到 whisper-server 可执行文件。", _options.ServerExecutablePath);
        }

        if (string.IsNullOrWhiteSpace(_options.ModelPath) || !File.Exists(_options.ModelPath))
        {
            SetStatus(WhisperServerStatus.Failed);
            throw new FileNotFoundException("未找到 Whisper 模型文件。", _options.ModelPath);
        }
    }

    private async Task StopManagedProcessAsync(CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is { HasExited: false })
        {
            process.SuppressExitNotification();
            process.Kill();
            await process.WaitForExitAsync(cancellationToken);
        }

        if (process is not null)
        {
            if (ReferenceEquals(_process, process))
            {
                _process = null;
            }

            process.Dispose();
        }

        SetStatus(WhisperServerStatus.Stopped);
    }

    public async Task HealthCheckAsync(CancellationToken cancellationToken)
    {
        if (!await TryProbeAsync(cancellationToken))
        {
            if (_process is null or { HasExited: true })
            {
                SetStatus(WhisperServerStatus.Stopped);
            }

            throw new InvalidOperationException($"无法连接到 whisper-server：{_options.BaseUri}");
        }

        SetStatus(WhisperServerStatus.Ready);
    }

    private async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_process is { HasExited: true })
            {
                SetStatus(WhisperServerStatus.Failed);
                throw CreateStartupFailureException(exitCode: _process.ExitCode);
            }

            if (await TryProbeAsync(cancellationToken))
            {
                return;
            }

            await Task.Delay(500, cancellationToken);
        }

        SetStatus(WhisperServerStatus.Failed);
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
        return await _healthProbe.IsHealthyAsync(_options.BaseUri, cancellationToken);
    }

    private void ResetProcessOutput()
    {
        lock (_outputLock)
        {
            _processOutput.Clear();
        }
    }

    private void AttachProcessOutputHandlers(IWhisperServerProcess process)
    {
        process.OutputReceived += AppendProcessOutput;
        process.ErrorReceived += AppendProcessOutput;
        process.BeginOutputRead();
    }

    private void OnManagedProcessExited(IWhisperServerProcess process)
    {
        if (ReferenceEquals(_process, process))
        {
            SetStatus(WhisperServerStatus.Failed);
        }
    }

    private bool IsStopRequested()
    {
        lock (_lifecycleLock)
        {
            return _pendingStopCount > 0;
        }
    }

    private void SetStatus(WhisperServerStatus status)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;
        StatusChanged?.Invoke(status);
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

internal sealed class HttpWhisperServerHealthProbe : IWhisperServerHealthProbe
{
    public async Task<bool> IsHealthyAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(2) };

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

internal sealed class SystemWhisperServerProcessFactory : IWhisperServerProcessFactory
{
    public IWhisperServerProcess Start(ProcessStartInfo startInfo)
    {
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 whisper-server。");
        return new SystemWhisperServerProcess(process);
    }
}

internal sealed class SystemWhisperServerProcess : IWhisperServerProcess
{
    private readonly Process _process;

    public SystemWhisperServerProcess(Process process)
    {
        _process = process;
        _process.EnableRaisingEvents = true;
        _process.Exited += (_, _) => Exited?.Invoke(this);
        _process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                OutputReceived?.Invoke(e.Data);
            }
        };
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                ErrorReceived?.Invoke(e.Data);
            }
        };
    }

    public event Action<IWhisperServerProcess>? Exited;
    public event Action<string>? OutputReceived;
    public event Action<string>? ErrorReceived;

    public bool HasExited => _process.HasExited;
    public int ExitCode => _process.ExitCode;

    public void SuppressExitNotification()
    {
        _process.EnableRaisingEvents = false;
    }

    public void Kill()
    {
        _process.Kill(entireProcessTree: true);
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken) =>
        _process.WaitForExitAsync(cancellationToken);

    public void BeginOutputRead()
    {
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public void Dispose()
    {
        _process.Dispose();
    }
}
