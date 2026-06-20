using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.Infrastructure;
using LocalAsrClient.Core;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly AppServices _services;
    private readonly Func<Task>? _onSettingsSaved;
    private string _modelPath = "";
    private string _whisperServerPath = "";
    private int _whisperServerPort = AppSettings.DefaultWhisperServerPort;

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

    public string DataDirectoryPath => LessAsrPaths.DataDirectory;

    public string LogsDirectoryPath => LessAsrPaths.LogsDirectory;

    public TranscriptRetentionPolicy TranscriptRetentionPolicy { get; set; } = TranscriptRetentionPolicy.SevenDays;

    public bool StartModelOnAppStartup { get; set; }

    public bool MinimizeToTrayOnClose { get; set; } = true;

    public string LastSavedAtText { get; private set; } = "";

    public ICommand BrowseModelPathCommand => new RelayCommand(BrowseModelPath);

    public ICommand BrowseWhisperServerPathCommand => new RelayCommand(BrowseWhisperServerPath);

    public ICommand SaveCommand => new AsyncRelayCommand(async () =>
    {
        if (WhisperServerPort is < 1 or > 65535)
        {
            throw new InvalidOperationException("whisper-server 端口必须在 1 到 65535 之间。");
        }

        await _services.SettingsStore.SaveAsync(new AppSettings(
            ModelPath,
            WhisperServerPath,
            WhisperServerPort,
            TranscriptRetentionPolicy,
            StartModelOnAppStartup,
            MinimizeToTrayOnClose), CancellationToken.None);
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
        TranscriptRetentionPolicy = settings.TranscriptRetentionPolicy;
        StartModelOnAppStartup = settings.StartModelOnAppStartup;
        MinimizeToTrayOnClose = settings.MinimizeToTrayOnClose;
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
