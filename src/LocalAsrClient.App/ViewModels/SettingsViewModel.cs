using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.Infrastructure;
using LocalAsrClient.Core;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;
using WhisperServerThreads = LocalAsrClient.Core.Asr.WhisperServerThreadCount;

namespace LocalAsrClient.App.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly AppServices _services;
    private readonly Func<Task>? _onSettingsSaved;
    private string _modelPath = "";
    private string _whisperServerPath = "";
    private int _whisperServerPort = AppSettings.DefaultWhisperServerPort;
    private int _whisperServerThreadCount = WhisperServerThreads.RecommendForCurrentMachine();
    private bool _useAutoWhisperServerThreadCount = true;
    private TranscriptRetentionPolicy _transcriptRetentionPolicy = TranscriptRetentionPolicy.SevenDays;
    private bool _startModelOnAppStartup;
    private bool _minimizeToTrayOnClose = true;
    private string _preferredTranscriptionLanguageId = TranscriptionLanguageCatalog.DefaultId;
    private bool _isLoading;
    private bool _hasUnsavedChanges;
    private bool _isSaving;

    public SettingsViewModel(AppServices services, Func<Task>? onSettingsSaved = null)
    {
        _services = services;
        _onSettingsSaved = onSettingsSaved;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ModelPath
    {
        get => _modelPath;
        set => SetField(ref _modelPath, value);
    }

    public string WhisperServerPath
    {
        get => _whisperServerPath;
        set => SetField(ref _whisperServerPath, value);
    }

    public int WhisperServerPort
    {
        get => _whisperServerPort;
        set => SetField(ref _whisperServerPort, value);
    }

    public int RecommendedWhisperServerThreadCount => WhisperServerThreads.RecommendForCurrentMachine();

    public int WhisperServerThreadCount
    {
        get => _whisperServerThreadCount;
        set
        {
            if (SetField(ref _whisperServerThreadCount, value))
            {
                _useAutoWhisperServerThreadCount = false;
            }
        }
    }

    public string DataDirectoryPath => LessAsrPaths.DataDirectory;

    public string LogsDirectoryPath => LessAsrPaths.LogsDirectory;

    public TranscriptRetentionPolicy TranscriptRetentionPolicy
    {
        get => _transcriptRetentionPolicy;
        set => SetField(ref _transcriptRetentionPolicy, value);
    }

    public bool StartModelOnAppStartup
    {
        get => _startModelOnAppStartup;
        set => SetField(ref _startModelOnAppStartup, value);
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

    public string SaveButtonText => IsSaving
        ? "正在保存…"
        : HasUnsavedChanges ? "保存设置" : "已保存";

    public ICommand BrowseModelPathCommand => new RelayCommand(BrowseModelPath);

    public ICommand BrowseWhisperServerPathCommand => new RelayCommand(BrowseWhisperServerPath);

    public ICommand ResetWhisperServerThreadCountCommand => new RelayCommand(ResetWhisperServerThreadCount);

    public ICommand SaveCommand => new AsyncRelayCommand(async () =>
    {
        if (!CanSave)
        {
            return;
        }

        IsSaving = true;

        try
        {
        if (WhisperServerPort is < 1 or > 65535)
        {
            throw new InvalidOperationException("whisper-server 端口必须在 1 到 65535 之间。");
        }

        if (WhisperServerThreadCount is < 1)
        {
            throw new InvalidOperationException("whisper-server 线程数必须大于 0。");
        }

        await _services.SettingsStore.SaveAsync(new AppSettings(
            ModelPath,
            WhisperServerPath,
            WhisperServerPort,
            TranscriptRetentionPolicy,
            StartModelOnAppStartup,
            MinimizeToTrayOnClose,
            _useAutoWhisperServerThreadCount ? null : WhisperServerThreadCount,
            PreferredTranscriptionLanguageId), CancellationToken.None);
        await _services.ApplyServerOptionsFromSettingsAsync();
        LastSavedAtText = $"上次保存：{DateTime.Now:HH:mm:ss}";
        OnPropertyChanged(nameof(LastSavedAtText));
        HasUnsavedChanges = false;
        if (_onSettingsSaved is not null)
        {
            await _onSettingsSaved();
        }
        }
        finally
        {
            IsSaving = false;
        }
    }, "保存设置失败");

    public void ResetSaveFeedback()
    {
        if (string.IsNullOrEmpty(LastSavedAtText))
        {
            return;
        }

        LastSavedAtText = "";
        OnPropertyChanged(nameof(LastSavedAtText));
    }

    public async Task LoadAsync()
    {
        _isLoading = true;
        try
        {
            var settings = await _services.SettingsStore.LoadAsync(CancellationToken.None);
            ModelPath = settings.ModelPath;
            WhisperServerPath = settings.WhisperServerPath;
            WhisperServerPort = settings.WhisperServerPort;
            _useAutoWhisperServerThreadCount = settings.WhisperServerThreadCount is null;
            _whisperServerThreadCount = settings.WhisperServerThreadCount
                ?? WhisperServerThreads.RecommendForCurrentMachine();
            OnPropertyChanged(nameof(WhisperServerThreadCount));
            OnPropertyChanged(nameof(RecommendedWhisperServerThreadCount));
            TranscriptRetentionPolicy = settings.TranscriptRetentionPolicy;
            StartModelOnAppStartup = settings.StartModelOnAppStartup;
            MinimizeToTrayOnClose = settings.MinimizeToTrayOnClose;
            PreferredTranscriptionLanguageId = settings.PreferredTranscriptionLanguageId;
        }
        finally
        {
            _isLoading = false;
            HasUnsavedChanges = false;
        }
    }

    private void ResetWhisperServerThreadCount()
    {
        _useAutoWhisperServerThreadCount = true;
        if (_whisperServerThreadCount != RecommendedWhisperServerThreadCount)
        {
            WhisperServerThreadCount = RecommendedWhisperServerThreadCount;
        }
        else
        {
            OnPropertyChanged(nameof(WhisperServerThreadCount));
        }
    }

    private void BrowseModelPath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 Whisper 模型文件",
            Filter = "Whisper 模型 (*.bin)|*.bin|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            FileName = ModelPath
        };

        if (dialog.ShowDialog() == true)
        {
            ModelPath = dialog.FileName;
        }
    }

    private void BrowseWhisperServerPath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 whisper-server 可执行文件",
            Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            FileName = WhisperServerPath
        };

        if (dialog.ShowDialog() == true)
        {
            WhisperServerPath = dialog.FileName;
        }
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        MarkDirty();
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

    private void MarkDirty()
    {
        if (_isLoading)
        {
            return;
        }

        HasUnsavedChanges = true;
        if (!string.IsNullOrEmpty(LastSavedAtText))
        {
            LastSavedAtText = "";
            OnPropertyChanged(nameof(LastSavedAtText));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
