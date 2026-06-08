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
    private string _modelName = "未选择模型";
    private string _modelPath = "";
    private string _serviceState = "未启动";
    private string _serviceAddress = "";
    private string _lastMessage = "";
    private string _lastError = "";

    public ModelViewModel(AppServices services)
    {
        _services = services;
        RefreshFromManager();
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

    public ICommand StartCommand => new AsyncRelayCommand(
        () => ExecuteServiceActionAsync(
            () => _services.ServerManager.EnsureStartedAsync(CancellationToken.None),
            "服务已就绪。",
            "启动模型服务失败"),
        "启动模型服务失败");

    public ICommand StopCommand => new AsyncRelayCommand(
        () => ExecuteServiceActionAsync(
            () => _services.ServerManager.StopAsync(CancellationToken.None),
            "已停止本程序托管的 whisper-server 进程。",
            "停止模型服务失败"),
        "停止模型服务失败");

    public ICommand RestartCommand => new AsyncRelayCommand(
        () => ExecuteServiceActionAsync(async () =>
        {
            await _services.ServerManager.StopAsync(CancellationToken.None);
            await _services.ServerManager.EnsureStartedAsync(CancellationToken.None);
        }, "服务已重启。", "重启模型服务失败"),
        "重启模型服务失败");

    public ICommand HealthCheckCommand => new AsyncRelayCommand(
        () => ExecuteServiceActionAsync(
            () => _services.ServerManager.HealthCheckAsync(CancellationToken.None),
            "健康检查通过，服务可达。",
            "模型服务健康检查失败"),
        "模型服务健康检查失败");

    public async Task InitializeAsync()
    {
        await RefreshFromSettingsAsync();
    }

    public async Task RefreshFromSettingsAsync()
    {
        await _services.ApplyServerOptionsFromSettingsAsync();
        var settings = await _services.SettingsStore.LoadAsync(CancellationToken.None);
        ModelPath = settings.ModelPath;
        ModelName = string.IsNullOrWhiteSpace(settings.ModelPath)
            ? "未选择模型"
            : Path.GetFileNameWithoutExtension(settings.ModelPath);
        RefreshFromManager();
    }

    private async Task ExecuteServiceActionAsync(Func<Task> action, string successMessage, string errorContext)
    {
        try
        {
            LastError = string.Empty;
            await _services.ApplyServerOptionsFromSettingsAsync();
            await action();
            LastMessage = successMessage;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            LastMessage = string.Empty;
            AppExceptionLogger.Report(ex, errorContext, showDialog: false);
        }
        finally
        {
            RefreshFromManager();
        }
    }

    private void RefreshFromManager()
    {
        ServiceState = WhisperServerStatusFormatter.ToDisplayText(_services.ServerManager.Status);
        ServiceAddress = _services.ServerManager.BaseUri.ToString();
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
