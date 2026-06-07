using System.Windows.Input;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.ViewModels;

public sealed class SettingsViewModel
{
    private readonly AppServices _services;

    public SettingsViewModel(AppServices services)
    {
        _services = services;
    }

    public string ModelPath { get; set; } = "";
    public string WhisperServerPath { get; set; } = "";
    public string DataDirectory { get; set; } = "";
    public TranscriptRetentionPolicy TranscriptRetentionPolicy { get; set; } = TranscriptRetentionPolicy.SevenDays;
    public bool StartModelOnAppStartup { get; set; }

    public ICommand SaveCommand => new RelayCommand(async () =>
    {
        await _services.SettingsStore.SaveAsync(new AppSettings(
            ModelPath,
            WhisperServerPath,
            DataDirectory,
            TranscriptRetentionPolicy,
            StartModelOnAppStartup), CancellationToken.None);
    });

    public async Task LoadAsync()
    {
        var settings = await _services.SettingsStore.LoadAsync(CancellationToken.None);
        ModelPath = settings.ModelPath;
        WhisperServerPath = settings.WhisperServerPath;
        DataDirectory = settings.DataDirectory;
        TranscriptRetentionPolicy = settings.TranscriptRetentionPolicy;
        StartModelOnAppStartup = settings.StartModelOnAppStartup;
    }

    private sealed class RelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        public RelayCommand(Func<Task> execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public async void Execute(object? parameter) => await _execute();
    }
}
