using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.Infrastructure;
using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.App.ViewModels;

public sealed class ModelViewModel : INotifyPropertyChanged
{
    private readonly AppServices _services;
    private string _modelName = "未配置";
    private string _modelPath = "";
    private string _serviceState = "未启动";
    private string _serviceAddress = "";
    private string _lastMessage = "";
    private string _lastError = "";

    public ModelViewModel(AppServices services)
    {
        _services = services;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ModelName
    {
        get => _modelName;
        private set => SetField(ref _modelName, value);
    }

    public string ModelPath
    {
        get => _modelPath;
        private set => SetField(ref _modelPath, value);
    }

    public string ServiceState
    {
        get => _serviceState;
        private set => SetField(ref _serviceState, value);
    }

    public string ServiceAddress
    {
        get => _serviceAddress;
        private set => SetField(ref _serviceAddress, value);
    }

    public string LastMessage
    {
        get => _lastMessage;
        private set => SetField(ref _lastMessage, value);
    }

    public string LastError
    {
        get => _lastError;
        private set => SetField(ref _lastError, value);
    }

    public ICommand StartCommand => new AsyncRelayCommand(StartAsync, "启动模型服务失败");

    public ICommand StopCommand => new AsyncRelayCommand(StopAsync, "停止模型服务失败");

    public ICommand RestartCommand => new AsyncRelayCommand(RestartAsync, "重启模型服务失败");

    public ICommand HealthCheckCommand => new AsyncRelayCommand(HealthCheckAsync, "模型服务健康检查失败");

    public async Task InitializeAsync()
    {
        await RefreshFromSettingsAsync();
        await SyncServiceStateFromProbeAsync();
    }

    public async Task RefreshFromSettingsAsync()
    {
        await _services.ApplyServerOptionsFromSettingsAsync();
        var settings = await _services.SettingsStore.LoadAsync(CancellationToken.None);
        ModelPath = string.IsNullOrWhiteSpace(settings.ModelPath) ? "（未配置）" : settings.ModelPath;
        ModelName = string.IsNullOrWhiteSpace(settings.ModelPath)
            ? "未配置"
            : Path.GetFileNameWithoutExtension(settings.ModelPath);
        ServiceAddress = _services.ServerManager.BaseUri.ToString();
        RefreshServiceState();
    }

    private async Task StartAsync()
    {
        await RunServiceActionAsync(
            "正在启动服务…",
            () => _services.ServerManager.EnsureStartedAsync(CancellationToken.None),
            "服务已就绪。");
    }

    private async Task StopAsync()
    {
        LastError = "";
        LastMessage = "正在停止服务…";

        try
        {
            await _services.ApplyServerOptionsFromSettingsAsync();
            await _services.ServerManager.StopAsync(CancellationToken.None);

            try
            {
                await _services.ServerManager.HealthCheckAsync(CancellationToken.None);
                LastMessage = "已停止本客户端托管的服务。检测到端口上仍有服务在运行（可能由外部启动）。";
            }
            catch (InvalidOperationException)
            {
                LastMessage = "服务已停止。";
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LastMessage = "停止模型服务失败。";
            AppExceptionLogger.Report(ex, "停止模型服务失败");
        }
        finally
        {
            RefreshServiceState();
        }
    }

    private async Task RestartAsync()
    {
        await RunServiceActionAsync(
            "正在重启服务…",
            async () =>
            {
                await _services.ServerManager.StopAsync(CancellationToken.None);
                _services.RefreshTranscribeHttpClient();
                await _services.ServerManager.EnsureStartedAsync(CancellationToken.None);
            },
            "服务已重启并就绪。");
    }

    private async Task HealthCheckAsync()
    {
        LastError = "";
        LastMessage = "正在执行健康检查…";

        try
        {
            await _services.ApplyServerOptionsFromSettingsAsync();
            await _services.ServerManager.HealthCheckAsync(CancellationToken.None);
            LastMessage = "健康检查通过。";
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LastMessage = "健康检查失败。";
            AppExceptionLogger.Report(ex, "模型服务健康检查失败");
        }
        finally
        {
            await SyncServiceStateFromProbeAsync();
        }
    }

    private async Task RunServiceActionAsync(string inProgressMessage, Func<Task> action, string successMessage)
    {
        LastError = "";
        LastMessage = inProgressMessage;

        try
        {
            await _services.ApplyServerOptionsFromSettingsAsync();
            await action();
            LastMessage = successMessage;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LastMessage = "操作失败。";
            AppExceptionLogger.Report(ex, inProgressMessage.TrimEnd('…'));
        }
        finally
        {
            RefreshServiceState();
        }
    }

    private async Task SyncServiceStateFromProbeAsync()
    {
        try
        {
            await _services.ServerManager.HealthCheckAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
        }

        RefreshServiceState();
    }

    private void RefreshServiceState()
    {
        ServiceState = ToChineseStatus(_services.ServerManager.Status);
        ServiceAddress = _services.ServerManager.BaseUri.ToString();
    }

    private static string ToChineseStatus(WhisperServerStatus status)
    {
        return status switch
        {
            WhisperServerStatus.Stopped => "已停止",
            WhisperServerStatus.Starting => "启动中",
            WhisperServerStatus.Ready => "已就绪",
            WhisperServerStatus.Transcribing => "识别中",
            WhisperServerStatus.Failed => "启动失败",
            _ => status.ToString()
        };
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
