using System.Windows.Input;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.Infrastructure;

namespace LocalAsrClient.App.ViewModels;

public sealed class ModelViewModel
{
    private readonly AppServices _services;

    public ModelViewModel(AppServices services)
    {
        _services = services;
    }

    public string ServiceState => _services.ServerManager.Status.ToString();
    public string ServiceAddress => _services.ServerManager.BaseUri.ToString();

    public ICommand StartCommand => new AsyncRelayCommand(
        () => _services.ServerManager.EnsureStartedAsync(CancellationToken.None),
        "启动模型服务失败");

    public ICommand StopCommand => new AsyncRelayCommand(
        () => _services.ServerManager.StopAsync(CancellationToken.None),
        "停止模型服务失败");

    public ICommand RestartCommand => new AsyncRelayCommand(async () =>
    {
        await _services.ServerManager.StopAsync(CancellationToken.None);
        await _services.ServerManager.EnsureStartedAsync(CancellationToken.None);
    }, "重启模型服务失败");

    public ICommand HealthCheckCommand => new AsyncRelayCommand(
        () => _services.ServerManager.EnsureStartedAsync(CancellationToken.None),
        "模型服务健康检查失败");
}
