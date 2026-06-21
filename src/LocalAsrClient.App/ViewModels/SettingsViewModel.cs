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

    public TranscriptRetentionPolicy TranscriptRetentionPolicy { get; set; } = TranscriptRetentionPolicy.SevenDays;

    public bool StartModelOnAppStartup { get; set; }

    public bool MinimizeToTrayOnClose { get; set; } = true;

    public IReadOnlyList<TranscriptionLanguageOption> TranscriptionLanguageOptions { get; } =
        TranscriptionLanguageCatalog.All;

    public string PreferredTranscriptionLanguageId { get; set; } = TranscriptionLanguageCatalog.DefaultId;

    public string LastSavedAtText { get; private set; } = "";

    public ICommand BrowseModelPathCommand => new RelayCommand(BrowseModelPath);

    public ICommand BrowseWhisperServerPathCommand => new RelayCommand(BrowseWhisperServerPath);

    public ICommand ResetWhisperServerThreadCountCommand => new RelayCommand(ResetWhisperServerThreadCount);

    public ICommand SaveCommand => new AsyncRelayCommand(async () =>
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
        if (_onSettingsSaved is not null)
        {
            await _onSettingsSaved();
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
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
