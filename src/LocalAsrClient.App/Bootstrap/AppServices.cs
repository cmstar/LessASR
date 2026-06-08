using System.IO;
using System.Net.Http;
using LocalAsrClient.App.Audio;
using LocalAsrClient.App.Hotkeys;
using LocalAsrClient.App.Overlay;
using LocalAsrClient.App.TextInjection;
using LocalAsrClient.Core;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Dictation;
using LocalAsrClient.Core.Persistence;
using LocalAsrClient.Core.Utilities;

namespace LocalAsrClient.App.Bootstrap;

public sealed class AppServices : IAsyncDisposable
{
    private AppServices(
        SqliteDatabase database,
        SqliteSettingsStore settingsStore,
        SqliteStatsRepository statsRepository,
        SqliteTextHistoryRepository historyRepository,
        DictationOverlayWindow overlayWindow,
        RightCtrlHotkeyListener hotkeyListener,
        DictationOrchestrator orchestrator,
        WhisperServerProcessManager serverManager)
    {
        Database = database;
        SettingsStore = settingsStore;
        StatsRepository = statsRepository;
        HistoryRepository = historyRepository;
        OverlayWindow = overlayWindow;
        HotkeyListener = hotkeyListener;
        Orchestrator = orchestrator;
        ServerManager = serverManager;
    }

    public SqliteDatabase Database { get; }
    public SqliteSettingsStore SettingsStore { get; }
    public SqliteStatsRepository StatsRepository { get; }
    public SqliteTextHistoryRepository HistoryRepository { get; }
    public DictationOverlayWindow OverlayWindow { get; }
    public RightCtrlHotkeyListener HotkeyListener { get; }
    public DictationOrchestrator Orchestrator { get; }
    public WhisperServerProcessManager ServerManager { get; }

    public async Task ApplyServerOptionsFromSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await SettingsStore.LoadAsync(cancellationToken);
        ServerManager.UpdateOptions(new WhisperServerOptions(
            settings.WhisperServerPath,
            settings.ModelPath,
            "127.0.0.1",
            8080));
    }

    public static async Task<AppServices> CreateAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(LessAsrPaths.DataDirectory);
        var database = await SqliteDatabase.OpenAsync(LessAsrPaths.DatabasePath, cancellationToken);
        var settingsStore = new SqliteSettingsStore(database);
        var settings = await settingsStore.LoadAsync(cancellationToken);

        var options = new WhisperServerOptions(
            settings.WhisperServerPath,
            settings.ModelPath,
            "127.0.0.1",
            8080);
        var serverManager = new WhisperServerProcessManager(options);
        var httpClient = new HttpClient { BaseAddress = options.BaseUri };
        var backend = new ManagedWhisperServerBackend(serverManager, new WhisperServerClient(httpClient));

        var statsRepository = new SqliteStatsRepository(database);
        var historyRepository = new SqliteTextHistoryRepository(database);
        var recorder = new NAudioMemoryRecorder();
        var injector = new SendInputTextInjector();
        var overlayWindow = new DictationOverlayWindow();
        var hotkeyListener = new RightCtrlHotkeyListener();
        var orchestrator = new DictationOrchestrator(
            recorder,
            backend,
            injector,
            statsRepository,
            historyRepository,
            settingsStore,
            new SystemClock());

        hotkeyListener.Triggered += () =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await orchestrator.ToggleAsync(CancellationToken.None);
                }
                catch
                {
                }
            });
        };

        if (settings.StartModelOnAppStartup)
        {
            _ = serverManager.EnsureStartedAsync(CancellationToken.None);
        }

        return new AppServices(
            database,
            settingsStore,
            statsRepository,
            historyRepository,
            overlayWindow,
            hotkeyListener,
            orchestrator,
            serverManager);
    }

    public async ValueTask DisposeAsync()
    {
        HotkeyListener.Dispose();
        await ServerManager.StopAsync(CancellationToken.None);
        await Database.DisposeAsync();
    }
}
