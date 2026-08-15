using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.Infrastructure;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.ViewModels;

public sealed class ServiceViewModel : INotifyPropertyChanged
{
    private readonly ISettingsStore _settingsStore;
    private readonly IAsrServiceCoordinator _coordinator;
    private readonly IWhisperServerManager _localManager;
    private readonly Action _refreshLocalClient;
    private readonly Func<bool> _isDictationBusy;
    private readonly Func<RemoteApiProfile, bool> _confirmDelete;
    private Guid? _activeRemoteId;
    private string _modelPath = "";
    private string _whisperServerPath = "";
    private int _whisperServerPort = AppSettings.DefaultWhisperServerPort;
    private int _whisperServerThreadCount = LocalAsrClient.Core.Asr.WhisperServerThreadCount.RecommendForCurrentMachine();
    private bool _useAutoThreadCount = true;
    private bool _startModelOnAppStartup;
    private bool _isOperationInProgress;
    private string _lastMessage = "";
    private string _lastError = "";

    public ServiceViewModel(AppServices services, Func<RemoteApiProfile, bool>? confirmDelete = null)
        : this(
            services.SettingsStore,
            services.ServiceCoordinator,
            services.ServerManager,
            services.RefreshTranscribeHttpClient,
            () => services.IsDictationBusy,
            confirmDelete)
    {
        services.Orchestrator.StatusChanged += _ => RefreshAvailabilityOnUiThread();
        services.ContinuousDictationSession.Changed += _ => RefreshAvailabilityOnUiThread();
    }

    public ServiceViewModel(
        ISettingsStore settingsStore,
        IAsrServiceCoordinator coordinator,
        IWhisperServerManager localManager,
        Action refreshLocalClient,
        Func<bool> isDictationBusy,
        Func<RemoteApiProfile, bool>? confirmDelete = null)
    {
        _settingsStore = settingsStore;
        _coordinator = coordinator;
        _localManager = localManager;
        _refreshLocalClient = refreshLocalClient;
        _isDictationBusy = isDictationBusy;
        _confirmDelete = confirmDelete ?? (_ => true);
        _localManager.StatusChanged += OnLocalStatusChanged;
        AddRemoteCommand = new RelayCommand(AddRemoteProfile);
        BrowseModelPathCommand = new RelayCommand(BrowseModelPath);
        BrowseWhisperServerPathCommand = new RelayCommand(BrowseWhisperServerPath);
        ResetThreadCountCommand = new RelayCommand(ResetThreadCount);
        SaveLocalCommand = new AsyncRelayCommand(() => SaveLocalAsync(false), "保存本地服务失败");
        SaveAndRestartLocalCommand = new AsyncRelayCommand(() => SaveLocalAsync(true), "保存并重启本地服务失败");
        ActivateLocalCommand = new AsyncRelayCommand(ActivateLocalAsync, "切换本地服务失败");
        StartLocalCommand = new AsyncRelayCommand(StartLocalAsync, "启动本地服务失败");
        StopLocalCommand = new AsyncRelayCommand(StopLocalAsync, "停止本地服务失败");
        TestLocalCommand = new AsyncRelayCommand(TestLocalAsync, "测试本地服务失败");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RemoteServiceProfileViewModel> RemoteProfiles { get; } = [];

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

    public int WhisperServerThreadCount
    {
        get => _whisperServerThreadCount;
        set
        {
            if (SetField(ref _whisperServerThreadCount, value))
            {
                _useAutoThreadCount = false;
            }
        }
    }

    public int RecommendedThreadCount => WhisperServerThreadCountCatalogValue();

    public bool StartModelOnAppStartup
    {
        get => _startModelOnAppStartup;
        set => SetField(ref _startModelOnAppStartup, value);
    }

    public bool IsLocalActive => _activeRemoteId is null;
    public string LocalServiceStateText => ToChineseStatus(_localManager.Status);
    public string LocalServiceAddress => _localManager.BaseUri.ToString();
    public bool LocalIsRestartRequired => _localManager.IsRestartRequired;
    public bool IsOperationInProgress => _isOperationInProgress;
    public bool CanMutate => !_isOperationInProgress && !_isDictationBusy();
    public bool CanActivateLocal => CanMutate && !IsLocalActive;
    public bool CanOperateLocal => CanMutate && IsLocalActive;
    public bool CanSaveAndRestartLocal => CanOperateLocal && _localManager.Status != WhisperServerStatus.Stopped;
    public string ActiveServiceStatusText => _activeRemoteId is Guid activeId
        ? $"{RemoteProfiles.FirstOrDefault(profile => profile.Id == activeId)?.Name ?? "远程 API"} · 远程 API"
        : $"本地 Whisper · {LocalServiceStateText}";
    public string PrivacyNoticeText => IsLocalActive
        ? "音频仅在本机处理"
        : "音频发送至 API";
    public string LastMessage => _lastMessage;
    public string LastError => _lastError;

    public ICommand AddRemoteCommand { get; }
    public ICommand BrowseModelPathCommand { get; }
    public ICommand BrowseWhisperServerPathCommand { get; }
    public ICommand ResetThreadCountCommand { get; }
    public ICommand SaveLocalCommand { get; }
    public ICommand SaveAndRestartLocalCommand { get; }
    public ICommand ActivateLocalCommand { get; }
    public ICommand StartLocalCommand { get; }
    public ICommand StopLocalCommand { get; }
    public ICommand TestLocalCommand { get; }

    public async Task LoadAsync()
    {
        var settings = await _settingsStore.LoadAsync(CancellationToken.None);
        _activeRemoteId = settings.ActiveRemoteApiProfileId;
        _modelPath = settings.ModelPath;
        _whisperServerPath = settings.WhisperServerPath;
        _whisperServerPort = settings.WhisperServerPort;
        _useAutoThreadCount = settings.WhisperServerThreadCount is null;
        _whisperServerThreadCount = settings.WhisperServerThreadCount ?? WhisperServerThreadCountCatalogValue();
        _startModelOnAppStartup = settings.StartModelOnAppStartup;
        RaiseLocalConfigurationChanged();
        await ReloadRemoteProfilesAsync();
        RefreshAvailability();
    }

    public void AddRemoteProfile()
    {
        if (!CanMutate || RemoteProfiles.Any(profile => profile.IsNew))
        {
            return;
        }

        RemoteProfiles.Add(CreateRemoteCard(profile: null));
    }

    public async Task SaveLocalAsync(bool restart)
    {
        await RunLocalOperationAsync(async () =>
        {
            if (WhisperServerPort is < 1 or > 65535)
            {
                throw new InvalidOperationException("whisper-server 端口必须在 1 到 65535 之间。");
            }

            if (WhisperServerThreadCount < 1)
            {
                throw new InvalidOperationException("whisper-server 线程数必须大于 0。");
            }

            var settings = await _settingsStore.LoadAsync(CancellationToken.None);
            settings = settings with
            {
                ModelPath = ModelPath,
                WhisperServerPath = WhisperServerPath,
                WhisperServerPort = WhisperServerPort,
                WhisperServerThreadCount = _useAutoThreadCount ? null : WhisperServerThreadCount,
                StartModelOnAppStartup = StartModelOnAppStartup
            };
            await _settingsStore.SaveAsync(settings, CancellationToken.None);
            _localManager.UpdateOptions(ToOptions(settings));

            if (restart && IsLocalActive)
            {
                await _localManager.StopAsync(CancellationToken.None);
                _refreshLocalClient();
                await _localManager.EnsureStartedAsync(CancellationToken.None);
                SetMessage("本地配置已保存，服务已重启。");
            }
            else
            {
                if (!_localManager.IsRestartRequired)
                {
                    _refreshLocalClient();
                }

                SetMessage(_localManager.IsRestartRequired
                    ? "本地配置已保存，将在重启服务后生效。"
                    : "本地配置已保存。");
            }

            RaiseServiceStateChanged();
        });
    }

    public void RefreshAvailability()
    {
        OnPropertyChanged(nameof(CanMutate));
        OnPropertyChanged(nameof(CanActivateLocal));
        OnPropertyChanged(nameof(CanOperateLocal));
        OnPropertyChanged(nameof(CanSaveAndRestartLocal));
        foreach (var profile in RemoteProfiles)
        {
            profile.SetInteractionLocked(!CanMutate);
        }
    }

    private async Task ReloadRemoteProfilesAsync()
    {
        var profiles = await _coordinator.GetRemoteProfilesAsync(CancellationToken.None);
        RemoteProfiles.Clear();
        foreach (var profile in profiles)
        {
            RemoteProfiles.Add(CreateRemoteCard(profile));
        }

        RaiseServiceStateChanged();
    }

    private RemoteServiceProfileViewModel CreateRemoteCard(RemoteApiProfile? profile) => new(
        profile,
        profile?.Id == _activeRemoteId,
        SaveRemoteAsync,
        ActivateRemoteAsync,
        TestRemoteAsync,
        DeleteRemoteAsync,
        DiscardRemoteAsync);

    private async Task<RemoteApiProfile> SaveRemoteAsync(
        RemoteServiceProfileViewModel card,
        string? apiKey,
        ApiKeyUpdateMode apiKeyUpdateMode)
    {
        var input = new RemoteApiProfileInput(
            card.Name,
            card.Endpoint,
            card.Model,
            card.UseVocabulary,
            apiKey);
        if (card.Id is not Guid id)
        {
            return await _coordinator.CreateRemoteAsync(input, CancellationToken.None);
        }

        await _coordinator.UpdateRemoteAsync(id, input, apiKeyUpdateMode, CancellationToken.None);
        var profiles = await _coordinator.GetRemoteProfilesAsync(CancellationToken.None);
        return profiles.Single(profile => profile.Id == id);
    }

    private async Task ActivateRemoteAsync(Guid id)
    {
        await _coordinator.ActivateRemoteAsync(id, CancellationToken.None);
        _activeRemoteId = id;
        foreach (var profile in RemoteProfiles)
        {
            profile.SetActive(profile.Id == id);
        }

        RaiseServiceStateChanged();
    }

    private Task<AsrResult> TestRemoteAsync(Guid id) =>
        _coordinator.TestRemoteAsync(id, CancellationToken.None);

    private async Task DeleteRemoteAsync(Guid id)
    {
        var profile = (await _coordinator.GetRemoteProfilesAsync(CancellationToken.None))
            .Single(item => item.Id == id);
        if (!_confirmDelete(profile))
        {
            return;
        }

        await _coordinator.DeleteRemoteAsync(id, CancellationToken.None);
        var card = RemoteProfiles.Single(item => item.Id == id);
        RemoteProfiles.Remove(card);
    }

    private Task DiscardRemoteAsync(RemoteServiceProfileViewModel card)
    {
        RemoteProfiles.Remove(card);
        return Task.CompletedTask;
    }

    private async Task ActivateLocalAsync()
    {
        await RunLocalOperationAsync(async () =>
        {
            await _coordinator.ActivateLocalAsync(CancellationToken.None);
            _activeRemoteId = null;
            foreach (var profile in RemoteProfiles)
            {
                profile.SetActive(false);
            }

            SetMessage("已切换到本地 Whisper；将在下次听写或手动启动时加载。 ");
            RaiseServiceStateChanged();
        });
    }

    private Task StartLocalAsync() => RunLocalOperationAsync(async () =>
    {
        if (!IsLocalActive)
        {
            throw new InvalidOperationException("请先将本地 Whisper 设为当前服务。");
        }

        await _localManager.EnsureStartedAsync(CancellationToken.None);
        SetMessage("本地服务已就绪。");
    });

    private Task StopLocalAsync() => RunLocalOperationAsync(async () =>
    {
        await _localManager.StopAsync(CancellationToken.None);
        _refreshLocalClient();
        SetMessage("本地服务已停止。");
    });

    private Task TestLocalAsync() => RunLocalOperationAsync(async () =>
    {
        var shouldStopAfterTest = !IsLocalActive && _localManager.Status == WhisperServerStatus.Stopped;
        try
        {
            await _localManager.EnsureStartedAsync(CancellationToken.None);
            await _localManager.HealthCheckAsync(CancellationToken.None);
            SetMessage("测试成功，本地服务可用。");
        }
        finally
        {
            if (shouldStopAfterTest)
            {
                await _localManager.StopAsync(CancellationToken.None);
                _refreshLocalClient();
            }
        }
    });

    private async Task RunLocalOperationAsync(Func<Task> action)
    {
        if (!CanMutate)
        {
            SetError("听写进行中，暂不能更改服务配置。");
            return;
        }

        _isOperationInProgress = true;
        SetError("");
        RefreshAvailability();
        OnPropertyChanged(nameof(IsOperationInProgress));
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
        finally
        {
            _isOperationInProgress = false;
            OnPropertyChanged(nameof(IsOperationInProgress));
            RefreshAvailability();
            RaiseServiceStateChanged();
        }
    }

    private static WhisperServerOptions ToOptions(AppSettings settings) => new(
        settings.WhisperServerPath,
        settings.ModelPath,
        "127.0.0.1",
        settings.WhisperServerPort,
        settings.WhisperServerThreadCount);

    private void ResetThreadCount()
    {
        _useAutoThreadCount = true;
        _whisperServerThreadCount = WhisperServerThreadCountCatalogValue();
        OnPropertyChanged(nameof(WhisperServerThreadCount));
    }

    private static int WhisperServerThreadCountCatalogValue() =>
        LocalAsrClient.Core.Asr.WhisperServerThreadCount.RecommendForCurrentMachine();

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

    private void OnLocalStatusChanged(WhisperServerStatus status) => RefreshAvailabilityOnUiThread();

    private void RefreshAvailabilityOnUiThread()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(RefreshAvailability);
            return;
        }

        RefreshAvailability();
        RaiseServiceStateChanged();
    }

    private void RaiseLocalConfigurationChanged()
    {
        OnPropertyChanged(nameof(ModelPath));
        OnPropertyChanged(nameof(WhisperServerPath));
        OnPropertyChanged(nameof(WhisperServerPort));
        OnPropertyChanged(nameof(WhisperServerThreadCount));
        OnPropertyChanged(nameof(RecommendedThreadCount));
        OnPropertyChanged(nameof(StartModelOnAppStartup));
    }

    private void RaiseServiceStateChanged()
    {
        OnPropertyChanged(nameof(IsLocalActive));
        OnPropertyChanged(nameof(LocalServiceStateText));
        OnPropertyChanged(nameof(LocalServiceAddress));
        OnPropertyChanged(nameof(LocalIsRestartRequired));
        OnPropertyChanged(nameof(ActiveServiceStatusText));
        OnPropertyChanged(nameof(PrivacyNoticeText));
        RefreshAvailability();
    }

    private static string ToChineseStatus(WhisperServerStatus status) => status switch
    {
        WhisperServerStatus.Stopped => "已停止",
        WhisperServerStatus.Starting => "启动中",
        WhisperServerStatus.Ready => "已就绪",
        WhisperServerStatus.Transcribing => "识别中",
        WhisperServerStatus.Failed => "启动失败",
        _ => status.ToString()
    };

    private void SetMessage(string value)
    {
        _lastMessage = value.Trim();
        OnPropertyChanged(nameof(LastMessage));
    }

    private void SetError(string value)
    {
        _lastError = value;
        OnPropertyChanged(nameof(LastError));
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
