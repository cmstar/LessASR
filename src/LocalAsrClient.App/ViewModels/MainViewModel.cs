using LocalAsrClient.App.Bootstrap;
using LocalAsrClient.App.Overlay;
using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.App.ViewModels;

public sealed class MainViewModel
{
    private readonly AppServices _services;

    public MainViewModel(AppServices services)
    {
        _services = services;
        Status = new StatusViewModel();
        History = new HistoryViewModel();
        Stats = new StatsViewModel();
        Model = new ModelViewModel(services);
        Settings = new SettingsViewModel(services, Model.RefreshFromSettingsAsync);
        Debug = new DebugViewModel(services);
        _services.Orchestrator.StatusChanged += OnDictationStatusChanged;
        _ = LoadAsync();
    }

    public StatusViewModel Status { get; }
    public HistoryViewModel History { get; }
    public StatsViewModel Stats { get; }
    public ModelViewModel Model { get; }
    public SettingsViewModel Settings { get; }
    public DebugViewModel Debug { get; }

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
            DictationState.Ready => OverlayState.Ready,
            DictationState.Recording => OverlayState.Recording,
            DictationState.Transcribing => OverlayState.Transcribing,
            DictationState.Injecting => OverlayState.Transcribing,
            DictationState.ResultNeedsAction => OverlayState.ResultNeedsAction,
            DictationState.Error => OverlayState.Error,
            _ => OverlayState.Injected
        };
    }

    private async Task LoadAsync()
    {
        await Settings.LoadAsync();
        await Model.InitializeAsync();
        var history = await _services.HistoryRepository.GetRecentAsync(50, CancellationToken.None);
        History.Load(history);
        var end = DateOnly.FromDateTime(DateTime.Now);
        var start = end.AddDays(-30);
        var stats = await _services.StatsRepository.GetRangeAsync(start, end, CancellationToken.None);
        Stats.Load(stats);
    }
}
