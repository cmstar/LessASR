using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.Dialogs;
using LocalAsrClient.App.Infrastructure;
using LocalAsrClient.App.Overlay;
using LocalAsrClient.Core.Dictation;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.ViewModels;

public sealed class MainViewModel
{
    private readonly AppServices _services;
    private readonly SemaphoreSlim _historyRefreshLock = new(1, 1);

    public MainViewModel(AppServices services)
    {
        _services = services;
        Navigation = new MainNavigationViewModel();
        Status = new StatusViewModel();
        History = new HistoryViewModel(
            services.HistoryRepository.DeleteAsync,
            ConfirmHistoryDeletion);
        Stats = new StatsViewModel();
        Services = new ServiceViewModel(services, ConfirmRemoteServiceDeletion);
        Vocabulary = new VocabularyViewModel(
            services.VocabularyRepository,
            RequestVocabularyName,
            ConfirmVocabularyDeletion);
        Settings = new SettingsViewModel(
            services,
            ConfirmHistoryRetentionCleanup);
        Debug = new DebugViewModel(services);
        _services.Orchestrator.StatusChanged += OnDictationStatusChanged;
        _services.HistoryRepository.Changed += OnHistoryChanged;
        Navigation.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainNavigationViewModel.SelectedSection)
                && Navigation.SelectedSection == MainSection.History)
            {
                OnHistoryChanged();
            }
        };
        Initialization = LoadAsync();
    }

    public Task Initialization { get; }

    public string RuntimeBadgeText => _services.IsDemoMode ? "演示" : "";

    public StatusViewModel Status { get; }
    public MainNavigationViewModel Navigation { get; }
    public HistoryViewModel History { get; }
    public StatsViewModel Stats { get; }
    public ServiceViewModel Services { get; }
    public VocabularyViewModel Vocabulary { get; }
    public SettingsViewModel Settings { get; }
    public DebugViewModel Debug { get; }

    private static bool ConfirmHistoryDeletion(TextHistoryEntry entry) =>
        ConfirmationDialog.Confirm(
            System.Windows.Application.Current?.MainWindow,
            new ConfirmationDialogOptions
            {
                Title = "删除历史记录",
                Heading = "确定删除这条历史记录？",
                Message = "删除后将无法恢复，使用统计不会受到影响。",
                ConfirmText = "删除",
                Preview = entry.Text,
                Tone = ConfirmationDialogTone.Destructive
            });

    private static bool ConfirmHistoryRetentionCleanup(HistoryRetentionChange change)
    {
        var disabling = change.NewPolicy == TranscriptRetentionPolicy.Disabled;
        var heading = disabling
            ? "关闭历史记录并删除现有内容？"
            : "缩短保留时间并删除旧记录？";
        var message = disabling
            ? $"保存后将永久删除现有的 {change.DeleteCount} 条历史记录，并停止保存新的听写文本。使用统计不会受到影响。"
            : $"保留时间将从“{change.PreviousPolicyDisplayName}”缩短为“{change.NewPolicyDisplayName}”，并永久删除 {change.DeleteCount} 条超出新期限的历史记录。使用统计不会受到影响。";

        return ConfirmationDialog.Confirm(
            System.Windows.Application.Current?.MainWindow,
            new ConfirmationDialogOptions
            {
                Title = "清理历史记录",
                Heading = heading,
                Message = message,
                ConfirmText = "删除并保存",
                Tone = ConfirmationDialogTone.Destructive
            });
    }

    private static string? RequestVocabularyName(IReadOnlyList<string> existingNames) =>
        VocabularyNameDialog.Prompt(
            System.Windows.Application.Current?.MainWindow,
            existingNames);

    private static bool ConfirmVocabularyDeletion(VocabularyProfile profile) =>
        ConfirmationDialog.Confirm(
            System.Windows.Application.Current?.MainWindow,
            new ConfirmationDialogOptions
            {
                Title = "删除词汇表",
                Heading = $"确定删除“{profile.Name}”？",
                Message = profile.IsActive
                    ? "删除后将停止使用词汇表，后续听写不会发送 Prompt。"
                    : "删除后将无法恢复，其中的词条也会一并移除。",
                ConfirmText = "删除",
                Preview = profile.Name,
                Tone = ConfirmationDialogTone.Destructive
            });

    private static bool ConfirmRemoteServiceDeletion(RemoteApiProfile profile) =>
        ConfirmationDialog.Confirm(
            System.Windows.Application.Current?.MainWindow,
            new ConfirmationDialogOptions
            {
                Title = "删除远程 API",
                Heading = $"确定删除“{profile.Name}”？",
                Message = "删除后将无法恢复。已保存的 API Key 也会一并移除。",
                ConfirmText = "删除",
                Preview = profile.Endpoint,
                Tone = ConfirmationDialogTone.Destructive
            });

    private void OnDictationStatusChanged(DictationStatus status)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            Status.Apply(status);
            var overlayState = ToOverlayState(status.State);
            _services.OverlayWindow.ShowOverlay(
                overlayState,
                status.Message,
                status.ResultText ?? "",
                status.ErrorMessage);

            if (status.State == DictationState.Idle && status.Message is "已输入" or "已取消")
            {
                _services.InjectionTargetCapture.Clear();
                if (status.Message == "已输入")
                {
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(700)
                    };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        _services.OverlayWindow.HideOverlay();
                    };
                    timer.Start();
                }
                else
                {
                    _services.OverlayWindow.HideOverlay();
                }
            }
        });
    }

    private static OverlayState ToOverlayState(DictationState state)
    {
        return state switch
        {
            DictationState.EnsuringModelReady => OverlayState.LoadingModel,
            DictationState.Recording => OverlayState.Recording,
            DictationState.Transcribing => OverlayState.Transcribing,
            DictationState.Injecting => OverlayState.Transcribing,
            DictationState.ResultNeedsAction => OverlayState.ResultNeedsAction,
            DictationState.Error => OverlayState.Error,
            _ => OverlayState.Injected
        };
    }

    private void OnHistoryChanged()
    {
        _ = RefreshHistoryAsync();
    }

    private async Task RefreshHistoryAsync()
    {
        await _historyRefreshLock.WaitAsync();
        try
        {
            var history = await _services.HistoryRepository.GetRecentAsync(50, CancellationToken.None);
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                History.Load(history);
            }
            else
            {
                await dispatcher.InvokeAsync(() => History.Load(history));
            }
        }
        catch (Exception ex)
        {
            AppExceptionLogger.Report(ex, "刷新历史记录失败", showDialog: false);
        }
        finally
        {
            _historyRefreshLock.Release();
        }
    }

    private async Task LoadAsync()
    {
        await Vocabulary.LoadAsync();
        await Settings.LoadAsync();
        await Services.LoadAsync();
        await RefreshHistoryAsync();
        var end = DateOnly.FromDateTime(DateTime.Now);
        var start = end.AddDays(-(StatsViewModel.SummaryDayCount - 1));
        var stats = await _services.StatsRepository.GetRangeAsync(start, end, CancellationToken.None);
        Stats.Load(stats);
    }
}
