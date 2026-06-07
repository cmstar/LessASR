using System.Windows.Input;
using LocalAsrClient.App.Bootstrap;

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

    public ICommand StartCommand => new RelayCommand(async () => await _services.ServerManager.EnsureStartedAsync(CancellationToken.None));
    public ICommand StopCommand => new RelayCommand(async () => await _services.ServerManager.StopAsync(CancellationToken.None));
    public ICommand RestartCommand => new RelayCommand(async () =>
    {
        await _services.ServerManager.StopAsync(CancellationToken.None);
        await _services.ServerManager.EnsureStartedAsync(CancellationToken.None);
    });
    public ICommand HealthCheckCommand => new RelayCommand(async () => await _services.ServerManager.EnsureStartedAsync(CancellationToken.None));

    private sealed class RelayCommand : ICommand
    {
        private readonly Func<Task> _execute;

        public RelayCommand(Func<Task> execute)
        {
            _execute = execute;
        }

        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter) => await _execute();
    }
}
