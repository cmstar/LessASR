using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.Infrastructure;
using LocalAsrClient.Core;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ISettingsStore _settingsStore;
    private readonly ITextHistoryRepository _historyRepository;
    private readonly Func<HistoryRetentionChange, bool> _confirmHistoryCleanup;
    private readonly string _dataDirectoryPath;
    private readonly string _logsDirectoryPath;
    private TranscriptRetentionPolicy _transcriptRetentionPolicy = TranscriptRetentionPolicy.SevenDays;
    private bool _minimizeToTrayOnClose = true;
    private string _preferredTranscriptionLanguageId = TranscriptionLanguageCatalog.DefaultId;
    private bool _isLoading;
    private bool _hasUnsavedChanges;
    private bool _isSaving;

    public SettingsViewModel(
        AppServices services,
        Func<HistoryRetentionChange, bool>? confirmHistoryCleanup = null)
        : this(
            services.SettingsStore,
            services.HistoryRepository,
            confirmHistoryCleanup,
            services.Paths.DataDirectory,
            services.Paths.LogsDirectory)
    {
    }

    public SettingsViewModel(
        ISettingsStore settingsStore,
        ITextHistoryRepository historyRepository,
        Func<HistoryRetentionChange, bool>? confirmHistoryCleanup = null,
        string? dataDirectoryPath = null,
        string? logsDirectoryPath = null)
    {
        _settingsStore = settingsStore;
        _historyRepository = historyRepository;
        _confirmHistoryCleanup = confirmHistoryCleanup ?? (_ => false);
        _dataDirectoryPath = dataDirectoryPath ?? LessAsrPaths.DataDirectory;
        _logsDirectoryPath = logsDirectoryPath ?? LessAsrPaths.LogsDirectory;
        SaveCommand = new AsyncRelayCommand(SaveAsync, "保存设置失败");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DataDirectoryPath => _dataDirectoryPath;
    public string LogsDirectoryPath => _logsDirectoryPath;

    public TranscriptRetentionPolicy TranscriptRetentionPolicy
    {
        get => _transcriptRetentionPolicy;
        set => SetField(ref _transcriptRetentionPolicy, value);
    }

    public bool MinimizeToTrayOnClose
    {
        get => _minimizeToTrayOnClose;
        set => SetField(ref _minimizeToTrayOnClose, value);
    }

    public IReadOnlyList<TranscriptionLanguageOption> TranscriptionLanguageOptions { get; } =
        TranscriptionLanguageCatalog.All;

    public string PreferredTranscriptionLanguageId
    {
        get => _preferredTranscriptionLanguageId;
        set => SetField(ref _preferredTranscriptionLanguageId, value);
    }

    public string LastSavedAtText { get; private set; } = "";

    public bool HasUnsavedChanges
    {
        get => _hasUnsavedChanges;
        private set
        {
            if (SetStateField(ref _hasUnsavedChanges, value))
            {
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(SaveButtonText));
            }
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetStateField(ref _isSaving, value))
            {
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(SaveButtonText));
            }
        }
    }

    public bool CanSave => HasUnsavedChanges && !IsSaving;
    public string SaveButtonText => IsSaving ? "正在保存…" : HasUnsavedChanges ? "保存设置" : "已保存";
    public ICommand SaveCommand { get; }

    public async Task SaveAsync()
    {
        if (!CanSave)
        {
            return;
        }

        IsSaving = true;
        try
        {
            var latestSettings = await _settingsStore.LoadAsync(CancellationToken.None);
            var now = DateTimeOffset.Now;
            var retentionChange = await GetHistoryRetentionChangeAsync(
                latestSettings.TranscriptRetentionPolicy,
                TranscriptRetentionPolicy,
                now);
            if (retentionChange is not null && !_confirmHistoryCleanup(retentionChange))
            {
                return;
            }

            await _settingsStore.UpdateAsync(settings => settings with
            {
                TranscriptRetentionPolicy = TranscriptRetentionPolicy,
                MinimizeToTrayOnClose = MinimizeToTrayOnClose,
                PreferredTranscriptionLanguageId = PreferredTranscriptionLanguageId
            }, CancellationToken.None);
            await _historyRepository.PruneAsync(now, TranscriptRetentionPolicy, CancellationToken.None);
            LastSavedAtText = $"上次保存：{DateTime.Now:HH:mm:ss}";
            OnPropertyChanged(nameof(LastSavedAtText));
            HasUnsavedChanges = false;
        }
        finally
        {
            IsSaving = false;
        }
    }

    public async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            var settings = await _settingsStore.LoadAsync(CancellationToken.None);
            TranscriptRetentionPolicy = settings.TranscriptRetentionPolicy;
            MinimizeToTrayOnClose = settings.MinimizeToTrayOnClose;
            PreferredTranscriptionLanguageId = settings.PreferredTranscriptionLanguageId;
        }
        finally
        {
            _isLoading = false;
            HasUnsavedChanges = false;
        }
    }

    private async Task<HistoryRetentionChange?> GetHistoryRetentionChangeAsync(
        TranscriptRetentionPolicy previousPolicy,
        TranscriptRetentionPolicy newPolicy,
        DateTimeOffset now)
    {
        if (!HistoryRetentionChange.IsShortening(previousPolicy, newPolicy))
        {
            return null;
        }

        var deleteCount = await _historyRepository.CountPrunableAsync(now, newPolicy, CancellationToken.None);
        return deleteCount > 0
            ? new HistoryRetentionChange(previousPolicy, newPolicy, deleteCount)
            : null;
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        if (!_isLoading)
        {
            HasUnsavedChanges = true;
            LastSavedAtText = "";
            OnPropertyChanged(nameof(LastSavedAtText));
        }

        return true;
    }

    private bool SetStateField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
