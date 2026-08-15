using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.Infrastructure;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.ViewModels;

public sealed record ModelProviderListItem(
    string Name,
    string ProviderTypeText,
    bool IsActive,
    bool IsSelected,
    RemoteServiceProfileViewModel? RemoteProfile)
{
    public bool IsLocal => RemoteProfile is null;
    public bool CanDelete => !IsLocal;
}

public sealed class ServiceViewModel : INotifyPropertyChanged
{
    private readonly ISettingsStore _settingsStore;
    private readonly IAsrServiceCoordinator _coordinator;
    private readonly IWhisperServerManager _localManager;
    private readonly Action _refreshLocalClient;
    private readonly Func<bool> _isDictationBusy;
    private readonly Func<RemoteApiProfile, bool> _confirmDelete;
    private readonly AsrActivityGate _activityGate;
    private Guid? _activeRemoteId;
    private string _modelPath = "";
    private string _whisperServerPath = "";
    private int _whisperServerPort = AppSettings.DefaultWhisperServerPort;
    private string _whisperServerThreadCountText = "";
    private bool _startModelOnAppStartup;
    private bool _isOperationInProgress;
    private string _lastMessage = "";
    private string _lastError = "";
    private RemoteServiceProfileViewModel? _selectedRemoteProfile;

    public ServiceViewModel(AppServices services, Func<RemoteApiProfile, bool>? confirmDelete = null)
        : this(
            services.SettingsStore,
            services.ServiceCoordinator,
            services.ServerManager,
            services.RefreshTranscribeHttpClient,
            () => services.IsDictationBusy,
            confirmDelete,
            services.ActivityGate)
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
        Func<RemoteApiProfile, bool>? confirmDelete = null,
        AsrActivityGate? activityGate = null)
    {
        _settingsStore = settingsStore;
        _coordinator = coordinator;
        _localManager = localManager;
        _refreshLocalClient = refreshLocalClient;
        _isDictationBusy = isDictationBusy;
        _confirmDelete = confirmDelete ?? (_ => true);
        _activityGate = activityGate ?? new AsrActivityGate();
        _localManager.StatusChanged += OnLocalStatusChanged;
        AddRemoteCommand = new RelayCommand(AddRemoteProfile);
        SelectModelProviderCommand = new RelayCommand<ModelProviderListItem>(SelectModelProvider);
        BrowseModelPathCommand = new RelayCommand(BrowseModelPath);
        BrowseWhisperServerPathCommand = new RelayCommand(BrowseWhisperServerPath);
        SaveLocalCommand = new AsyncRelayCommand(SaveLocalAsync, "保存本地服务失败");
        ActivateLocalCommand = new AsyncRelayCommand(ActivateLocalAsync, "切换本地服务失败");
        StartLocalCommand = new AsyncRelayCommand(StartLocalAsync, "启动本地服务失败");
        StopLocalCommand = new AsyncRelayCommand(StopLocalAsync, "停止本地服务失败");
        TestLocalCommand = new AsyncRelayCommand(TestLocalAsync, "测试本地服务失败");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<RemoteServiceProfileViewModel> RemoteProfiles { get; } = [];
    public ObservableCollection<ModelProviderListItem> ModelProviders { get; } = [];

    public RemoteServiceProfileViewModel? SelectedRemoteProfile => _selectedRemoteProfile;
    public bool IsLocalSelected => _selectedRemoteProfile is null;
    public bool IsRemoteSelected => _selectedRemoteProfile is not null;

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

    public string WhisperServerThreadCountText
    {
        get => _whisperServerThreadCountText;
        set => SetField(ref _whisperServerThreadCountText, value);
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
    public bool IsOperationInProgress => _isOperationInProgress;
    public bool CanMutate => !_isOperationInProgress && !_isDictationBusy();
    public bool CanActivateLocal => CanMutate && !IsLocalActive;
    public bool CanOperateLocal => CanMutate && IsLocalActive;
    public string ActiveServiceStatusText => _activeRemoteId is Guid activeId
        ? $"{RemoteProfiles.FirstOrDefault(profile => profile.Id == activeId)?.Name ?? "远程 API"} · 远程 API"
        : $"本地 Whisper · {LocalServiceStateText}";
    public string PrivacyNoticeText => IsLocalActive
        ? "音频仅在本机处理"
        : "音频发送至 API";
    public string LastMessage => _lastMessage;
    public string LastError => _lastError;

    public ICommand AddRemoteCommand { get; }
    public ICommand SelectModelProviderCommand { get; }
    public ICommand BrowseModelPathCommand { get; }
    public ICommand BrowseWhisperServerPathCommand { get; }
    public ICommand SaveLocalCommand { get; }
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
        _whisperServerThreadCountText = settings.WhisperServerThreadCount?.ToString(CultureInfo.InvariantCulture) ?? "";
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

        var card = CreateRemoteCard(profile: null);
        RemoteProfiles.Add(card);
        ApplyModelSelection(card);
    }

    public void SelectModelProvider(ModelProviderListItem item)
    {
        ApplyModelSelection(item.RemoteProfile);
    }

    public async Task SaveLocalAsync()
    {
        await RunLocalOperationAsync(async () =>
        {
            if (WhisperServerPort is < 1 or > 65535)
            {
                throw new InvalidOperationException("whisper-server 端口必须在 1 到 65535 之间。");
            }

            var threadCount = ParseThreadCount();

            AppSettings? savedSettings = null;
            await _settingsStore.UpdateAsync(settings => savedSettings = settings with
            {
                ModelPath = ModelPath,
                WhisperServerPath = WhisperServerPath,
                WhisperServerPort = WhisperServerPort,
                WhisperServerThreadCount = threadCount,
                StartModelOnAppStartup = StartModelOnAppStartup
            }, CancellationToken.None);
            _localManager.UpdateOptions(ToOptions(savedSettings!));

            if (IsLocalActive && _localManager.IsRestartRequired)
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
        foreach (var profile in RemoteProfiles)
        {
            profile.SetInteractionLocked(!CanMutate);
        }
    }

    private async Task ReloadRemoteProfilesAsync()
    {
        var profiles = await _coordinator.GetRemoteProfilesAsync(CancellationToken.None);
        foreach (var card in RemoteProfiles)
        {
            card.PropertyChanged -= OnRemoteProfilePropertyChanged;
        }

        RemoteProfiles.Clear();
        foreach (var profile in profiles)
        {
            RemoteProfiles.Add(CreateRemoteCard(profile));
        }

        ApplyModelSelection(_activeRemoteId is Guid activeId
            ? RemoteProfiles.FirstOrDefault(profile => profile.Id == activeId)
            : null);
        RaiseServiceStateChanged();
    }

    private RemoteServiceProfileViewModel CreateRemoteCard(RemoteApiProfile? profile)
    {
        var card = new RemoteServiceProfileViewModel(
            profile,
            profile is not null && profile.Id == _activeRemoteId,
            SaveRemoteAsync,
            ActivateRemoteAsync,
            TestRemoteAsync,
            DeleteRemoteAsync,
            ClearRemoteApiKeyAsync,
            RunRemoteOperationAsync,
            DiscardRemoteAsync);
        card.PropertyChanged += OnRemoteProfilePropertyChanged;
        return card;
    }

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
            apiKey,
            card.ProxyUrl);
        if (card.Id is not Guid id)
        {
            return await _coordinator.CreateRemoteAsync(input, CancellationToken.None);
        }

        await _coordinator.UpdateRemoteAsync(id, input, apiKeyUpdateMode, CancellationToken.None);
        var profiles = await _coordinator.GetRemoteProfilesAsync(CancellationToken.None);
        var saved = profiles.Single(profile => profile.Id == id);
        if (_activeRemoteId == id)
        {
            OnPropertyChanged(nameof(ActiveServiceStatusText));
        }

        return saved;
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
        card.PropertyChanged -= OnRemoteProfilePropertyChanged;
        RemoteProfiles.Remove(card);
        if (ReferenceEquals(_selectedRemoteProfile, card))
        {
            ApplyModelSelection(remoteProfile: null);
        }
        else
        {
            RebuildModelProviders();
        }
    }

    private async Task ClearRemoteApiKeyAsync(Guid id)
    {
        var profile = (await _coordinator.GetRemoteProfilesAsync(CancellationToken.None))
            .Single(item => item.Id == id);
        var savedInput = new RemoteApiProfileInput(
            profile.Name,
            profile.Endpoint,
            profile.Model,
            profile.UseVocabulary,
            ApiKey: null,
            profile.ProxyUrl);
        await _coordinator.UpdateRemoteAsync(
            id,
            savedInput,
            ApiKeyUpdateMode.Clear,
            CancellationToken.None);
    }

    private Task DiscardRemoteAsync(RemoteServiceProfileViewModel card)
    {
        card.PropertyChanged -= OnRemoteProfilePropertyChanged;
        RemoteProfiles.Remove(card);
        if (ReferenceEquals(_selectedRemoteProfile, card))
        {
            ApplyModelSelection(remoteProfile: null);
        }
        else
        {
            RebuildModelProviders();
        }

        return Task.CompletedTask;
    }

    private async Task RunRemoteOperationAsync(Func<Task> action)
    {
        if (!CanMutate)
        {
            throw new InvalidOperationException("另一个服务操作正在进行，请稍后再试。");
        }

        _isOperationInProgress = true;
        OnPropertyChanged(nameof(IsOperationInProgress));
        RefreshAvailability();
        try
        {
            await action();
        }
        finally
        {
            _isOperationInProgress = false;
            OnPropertyChanged(nameof(IsOperationInProgress));
            RefreshAvailability();
            RaiseServiceStateChanged();
        }
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

            SetMessage("已启用本地 Whisper；将在下次听写或手动启动时加载。 ");
            RaiseServiceStateChanged();
        }, acquireActivityLease: false);
    }

    private Task StartLocalAsync() => RunLocalOperationAsync(async () =>
    {
        if (!IsLocalActive)
        {
            throw new InvalidOperationException("请先启用本地 Whisper。");
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

    private async Task RunLocalOperationAsync(
        Func<Task> action,
        bool acquireActivityLease = true)
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
            await using var activityLease = acquireActivityLease
                ? await _activityGate.TryEnterAsync(CancellationToken.None)
                : null;
            if (acquireActivityLease && activityLease is null)
            {
                throw new InvalidOperationException("听写进行中，暂不能更改本地服务。");
            }

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

    private static int WhisperServerThreadCountCatalogValue() =>
        LocalAsrClient.Core.Asr.WhisperServerThreadCount.RecommendForCurrentMachine();

    private int? ParseThreadCount()
    {
        if (string.IsNullOrWhiteSpace(WhisperServerThreadCountText))
        {
            return null;
        }

        if (!int.TryParse(
                WhisperServerThreadCountText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var threadCount)
            || threadCount < 1)
        {
            throw new InvalidOperationException("whisper-server 线程数必须是大于 0 的整数。");
        }

        return threadCount;
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

    private void OnLocalStatusChanged(WhisperServerStatus status) => RefreshAvailabilityOnUiThread();

    private void RefreshAvailabilityOnUiThread()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.BeginInvoke(RaiseServiceStateChanged);
            return;
        }

        RaiseServiceStateChanged();
    }

    private void RaiseLocalConfigurationChanged()
    {
        OnPropertyChanged(nameof(ModelPath));
        OnPropertyChanged(nameof(WhisperServerPath));
        OnPropertyChanged(nameof(WhisperServerPort));
        OnPropertyChanged(nameof(WhisperServerThreadCountText));
        OnPropertyChanged(nameof(RecommendedThreadCount));
        OnPropertyChanged(nameof(StartModelOnAppStartup));
    }

    private void RaiseServiceStateChanged()
    {
        OnPropertyChanged(nameof(IsLocalActive));
        OnPropertyChanged(nameof(LocalServiceStateText));
        OnPropertyChanged(nameof(LocalServiceAddress));
        OnPropertyChanged(nameof(ActiveServiceStatusText));
        OnPropertyChanged(nameof(PrivacyNoticeText));
        RebuildModelProviders();
        RefreshAvailability();
    }

    private void ApplyModelSelection(RemoteServiceProfileViewModel? remoteProfile)
    {
        if (ReferenceEquals(_selectedRemoteProfile, remoteProfile))
        {
            RebuildModelProviders();
            return;
        }

        _selectedRemoteProfile = remoteProfile;
        OnPropertyChanged(nameof(SelectedRemoteProfile));
        OnPropertyChanged(nameof(IsLocalSelected));
        OnPropertyChanged(nameof(IsRemoteSelected));
        RebuildModelProviders();
    }

    private void RebuildModelProviders()
    {
        ModelProviders.Clear();
        ModelProviders.Add(new ModelProviderListItem(
            "本地 Whisper",
            "whisper.cpp",
            IsLocalActive,
            IsLocalSelected,
            RemoteProfile: null));
        foreach (var profile in RemoteProfiles)
        {
            ModelProviders.Add(new ModelProviderListItem(
                profile.DisplayName,
                profile.ProviderTypeText,
                IsRemoteActive(profile),
                ReferenceEquals(profile, _selectedRemoteProfile),
                profile));
        }
    }

    private void OnRemoteProfilePropertyChanged(
        object? sender,
        PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(RemoteServiceProfileViewModel.IsActive)
            && sender is RemoteServiceProfileViewModel profile
            && profile.IsActive != IsRemoteActive(profile))
        {
            profile.SetActive(IsRemoteActive(profile));
            return;
        }

        if (eventArgs.PropertyName is nameof(RemoteServiceProfileViewModel.DisplayName)
            or nameof(RemoteServiceProfileViewModel.IsActive)
            or nameof(RemoteServiceProfileViewModel.Id))
        {
            RebuildModelProviders();
        }
    }

    private bool IsRemoteActive(RemoteServiceProfileViewModel profile) =>
        _activeRemoteId is Guid activeRemoteId && profile.Id == activeRemoteId;

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
