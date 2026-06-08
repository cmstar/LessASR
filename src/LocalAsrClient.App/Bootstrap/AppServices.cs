using System.IO;

using System.Net.Http;

using LocalAsrClient.App.Audio;

using LocalAsrClient.App.Infrastructure;

using LocalAsrClient.App.Hotkeys;

using LocalAsrClient.App.Overlay;

using LocalAsrClient.App.TextInjection;

using LocalAsrClient.Core;
using LocalAsrClient.Core.Abstractions;

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

        IHotkeyListener hotkeyListener,

        EscapeCancelListener escapeCancelListener,

        DictationOrchestrator orchestrator,

        InjectionTargetCapture injectionTargetCapture,

        HttpClient httpClient,

        WhisperServerProcessManager serverManager)

    {

        Database = database;

        SettingsStore = settingsStore;

        StatsRepository = statsRepository;

        HistoryRepository = historyRepository;

        OverlayWindow = overlayWindow;

        HotkeyListener = hotkeyListener;

        EscapeCancelListener = escapeCancelListener;

        Orchestrator = orchestrator;

        InjectionTargetCapture = injectionTargetCapture;

        HttpClient = httpClient;

        ServerManager = serverManager;

    }



    public SqliteDatabase Database { get; }

    public SqliteSettingsStore SettingsStore { get; }

    public SqliteStatsRepository StatsRepository { get; }

    public SqliteTextHistoryRepository HistoryRepository { get; }

    public DictationOverlayWindow OverlayWindow { get; }

    public IHotkeyListener HotkeyListener { get; }

    public EscapeCancelListener EscapeCancelListener { get; }

    public DictationOrchestrator Orchestrator { get; }

    public InjectionTargetCapture InjectionTargetCapture { get; }

    public HttpClient HttpClient { get; }

    public WhisperServerProcessManager ServerManager { get; }



    public async Task ApplyServerOptionsFromSettingsAsync(CancellationToken cancellationToken = default)

    {

        var settings = await SettingsStore.LoadAsync(cancellationToken);

        var options = new WhisperServerOptions(

            settings.WhisperServerPath,

            settings.ModelPath,

            "127.0.0.1",

            8080);

        ServerManager.UpdateOptions(options);

        HttpClient.BaseAddress = options.BaseUri;

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

        var injectionTargetCapture = new InjectionTargetCapture();

        var injector = new SendInputTextInjector(injectionTargetCapture);

        var overlayWindow = new DictationOverlayWindow();

        var hotkeyListener = new GlobalHotkeyListener(DictationHotkey.ToggleVirtualKey);

        var orchestrator = new DictationOrchestrator(

            recorder,

            backend,

            injector,

            statsRepository,

            historyRepository,

            settingsStore,

            new SystemClock());

        var escapeCancelListener = new EscapeCancelListener(() => orchestrator.State == DictationState.Recording);



        overlayWindow.CloseRequested += () =>

        {

            if (orchestrator.State == DictationState.Recording)

            {

                _ = Task.Run(async () =>

                {

                    try

                    {

                        await orchestrator.CancelRecordingAsync(CancellationToken.None);

                    }

                    catch (Exception ex)

                    {

                        AppExceptionLogger.Report(ex, "取消录音失败", showDialog: false);

                    }

                });

                return;

            }



            orchestrator.DismissOverlay();

        };



        hotkeyListener.Triggered += () =>
        {
            if (orchestrator.State is DictationState.Idle
                or DictationState.Ready
                or DictationState.Error
                or DictationState.ResultNeedsAction)
            {
                injectionTargetCapture.Capture();
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await orchestrator.ToggleAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    AppExceptionLogger.Report(ex, "听写热键处理失败", showDialog: false);
                }
            });
        };



        escapeCancelListener.CancelRequested += () =>

        {

            _ = Task.Run(async () =>

            {

                try

                {

                    await orchestrator.CancelRecordingAsync(CancellationToken.None);

                }

                catch (Exception ex)

                {

                    AppExceptionLogger.Report(ex, "Esc 取消录音失败", showDialog: false);

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

            escapeCancelListener,

            orchestrator,

            injectionTargetCapture,

            httpClient,

            serverManager);

    }



    public async ValueTask DisposeAsync()

    {

        HotkeyListener.Dispose();

        EscapeCancelListener.Dispose();

        await ServerManager.StopAsync(CancellationToken.None);

        await Database.DisposeAsync();

    }

}


