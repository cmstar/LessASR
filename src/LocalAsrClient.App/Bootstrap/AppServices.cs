using System.IO;

using LocalAsrClient.App.Audio;

using LocalAsrClient.App.Asr;

using LocalAsrClient.App.ContinuousDictation;

using LocalAsrClient.App.Diagnostics;

using LocalAsrClient.App.DemoMode;

using LocalAsrClient.App.Infrastructure;

using LocalAsrClient.App.Hotkeys;

using LocalAsrClient.App.Overlay;

using LocalAsrClient.App.Persistence;

using LocalAsrClient.App.Security;

using LocalAsrClient.App.TestMode;

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

        SqliteVocabularyRepository vocabularyRepository,

        SqliteStatsRepository statsRepository,

        NotifyingTextHistoryRepository historyRepository,

        DictationOverlayWindow overlayWindow,

        IHotkeyListener hotkeyListener,

        GlobalHotkeyListener inPlaceSegmentHotkeyListener,

        GlobalHotkeyListener continuousDictationHotkeyListener,

        EscapeCancelListener escapeCancelListener,

        InPlaceDictationOrchestrator inPlaceOrchestrator,

        ContinuousDictationSession inPlaceDictationSession,

        ContinuousDictationCoordinator continuousDictationCoordinator,

        ContinuousDictationSession continuousDictationSession,

        InjectionTargetCapture injectionTargetCapture,

        ResilientWhisperServerClient transcribeClient,

        RemoteHttpClientPool remoteHttpClientPool,

        SqliteRemoteApiProfileRepository remoteApiProfileRepository,

        SwitchableAsrBackend backendRouter,

        AsrServiceCoordinator serviceCoordinator,

        AsrActivityGate activityGate,

        WhisperServerProcessManager serverManager,

        IDiagnosticEventSink diagnosticSink,

        AppStartupOptions startupOptions)

    {

        Database = database;

        SettingsStore = settingsStore;

        VocabularyRepository = vocabularyRepository;

        StatsRepository = statsRepository;

        HistoryRepository = historyRepository;

        OverlayWindow = overlayWindow;

        HotkeyListener = hotkeyListener;

        InPlaceSegmentHotkeyListener = inPlaceSegmentHotkeyListener;

        ContinuousDictationHotkeyListener = continuousDictationHotkeyListener;

        EscapeCancelListener = escapeCancelListener;

        InPlaceOrchestrator = inPlaceOrchestrator;

        InPlaceDictationSession = inPlaceDictationSession;

        ContinuousDictationCoordinator = continuousDictationCoordinator;

        ContinuousDictationSession = continuousDictationSession;

        InjectionTargetCapture = injectionTargetCapture;

        TranscribeClient = transcribeClient;

        RemoteHttpClientPool = remoteHttpClientPool;

        RemoteApiProfileRepository = remoteApiProfileRepository;

        BackendRouter = backendRouter;

        ServiceCoordinator = serviceCoordinator;

        ActivityGate = activityGate;

        ServerManager = serverManager;

        DiagnosticSink = diagnosticSink;

        StartupOptions = startupOptions;

    }



    public SqliteDatabase Database { get; }

    public SqliteSettingsStore SettingsStore { get; }

    public SqliteVocabularyRepository VocabularyRepository { get; }

    public SqliteStatsRepository StatsRepository { get; }

    public NotifyingTextHistoryRepository HistoryRepository { get; }

    public DictationOverlayWindow OverlayWindow { get; }

    public IHotkeyListener HotkeyListener { get; }

    public GlobalHotkeyListener InPlaceSegmentHotkeyListener { get; }

    public GlobalHotkeyListener ContinuousDictationHotkeyListener { get; }

    public EscapeCancelListener EscapeCancelListener { get; }

    public InPlaceDictationOrchestrator InPlaceOrchestrator { get; }

    public ContinuousDictationSession InPlaceDictationSession { get; }

    public ContinuousDictationCoordinator ContinuousDictationCoordinator { get; }

    public ContinuousDictationSession ContinuousDictationSession { get; }

    public InjectionTargetCapture InjectionTargetCapture { get; }

    public ResilientWhisperServerClient TranscribeClient { get; }

    public RemoteHttpClientPool RemoteHttpClientPool { get; }

    public SqliteRemoteApiProfileRepository RemoteApiProfileRepository { get; }

    public SwitchableAsrBackend BackendRouter { get; }

    public AsrServiceCoordinator ServiceCoordinator { get; }

    public AsrActivityGate ActivityGate { get; }

    public WhisperServerProcessManager ServerManager { get; }

    public IDiagnosticEventSink DiagnosticSink { get; }

    public AppStartupOptions StartupOptions { get; }

    public LessAsrPathLayout Paths => StartupOptions.Paths;

    public bool IsDemoMode => StartupOptions.IsDemoMode;

    public bool IsDictationBusy => InPlaceOrchestrator.IsBusy
        || ContinuousDictationSession.IsBusy;



    public void RefreshTranscribeHttpClient()
    {
        TranscribeClient.Refresh(ServerManager.BaseUri);
    }



    public static async Task<AppServices> CreateAsync(
        string[]? startupArgs = null,
        CancellationToken cancellationToken = default)

    {
        return await CreateAsync(AppStartupOptions.Resolve(startupArgs), cancellationToken);
    }

    public static async Task<AppServices> CreateAsync(
        AppStartupOptions startupOptions,
        CancellationToken cancellationToken = default)

    {
        var paths = startupOptions.Paths;
        if (startupOptions.IsDemoMode)
        {
            ResetDemoDatabase(paths);
        }

        Directory.CreateDirectory(paths.DataDirectory);

        IDiagnosticEventSink diagnosticSink = startupOptions.DiagnosticsEnabled
            ? JsonlDiagnosticEventSink.Create(paths.DiagnosticsDirectory)
            : NullDiagnosticEventSink.Instance;

        var database = startupOptions.IsTestMode
            ? await SqliteDatabase.CreateInMemoryAsync()
            : await SqliteDatabase.OpenAsync(paths.DatabasePath, cancellationToken);

        if (startupOptions.IsDemoMode)
        {
            await DemoDataSeeder.SeedAsync(database, DateTimeOffset.Now, cancellationToken);
        }

        var settingsStore = new SqliteSettingsStore(database);

        var settings = await settingsStore.LoadAsync(cancellationToken);



        var options = new WhisperServerOptions(

            settings.WhisperServerPath,

            settings.ModelPath,

            "127.0.0.1",

            settings.WhisperServerPort,

            settings.WhisperServerThreadCount);

        var serverManager = new WhisperServerProcessManager(options, new AppFileLog());

        var transcribeClient = new ResilientWhisperServerClient(options.BaseUri, serverManager);

        IAsrBackend modeBackend = startupOptions.RuntimeMode switch
        {
            AppRuntimeMode.Test => new TestAsrBackend(TestModeOptions.DefaultAsrText),
            AppRuntimeMode.Demo => new DemoAsrBackend(DemoDataScenario.ContinuousDictationSegments),
            _ => new ManagedWhisperServerBackend(
                serverManager,
                transcribeClient,
                () => Path.GetFileNameWithoutExtension(serverManager.ActiveModelPath))
        };

        var backend = new SwitchableAsrBackend(modeBackend);
        var activityGate = new AsrActivityGate();

        var remoteApiProfileRepository = new SqliteRemoteApiProfileRepository(database);
        var secretProtector = new DpapiSecretProtector();
        var remoteHttpClientPool = new RemoteHttpClientPool();



        var clock = new SystemClock();

        var vocabularyRepository = new SqliteVocabularyRepository(database, clock);

        var statsRepository = new SqliteStatsRepository(database);

        var historyRepository = new NotifyingTextHistoryRepository(
            new SqliteTextHistoryRepository(database));

        IAudioRecorder singleRecorder = startupOptions.IsTestMode || startupOptions.IsDemoMode
            ? new SimulatedAudioRecorder()
            : new NAudioMemoryRecorder();

        IAudioRecorder continuousRecorder = startupOptions.IsTestMode || startupOptions.IsDemoMode
            ? new SimulatedAudioRecorder()
            : new NAudioMemoryRecorder();

        var transcriptionPipeline = new TranscriptionPipeline(
            backend,
            settingsStore,
            vocabularyRepository,
            new TranscriptionScriptPostProcessor(settingsStore),
            statsRepository,
            clock);

        var continuousSession = new ContinuousDictationSession(
            continuousRecorder,
            transcriptionPipeline,
            activityGate);

        var inPlaceSession = new ContinuousDictationSession(
            singleRecorder,
            transcriptionPipeline,
            activityGate);

        var continuousCoordinator = new ContinuousDictationCoordinator(
            continuousSession,
            historyRepository,
            settingsStore,
            clock,
            backend,
            startupOptions.IsDemoMode);

        var injectionTargetCapture = new InjectionTargetCapture(diagnosticSink);

        var injector = new SendInputTextInjector(injectionTargetCapture, diagnosticSink);

        var overlayWindow = new DictationOverlayWindow(diagnosticSink);

        if (singleRecorder is IAudioLevelSource audioLevelSource)
        {
            audioLevelSource.AudioLevelChanged += overlayWindow.SetRecordingLevel;
        }

        var hotkeyListener = GlobalHotkeyListener.CreateExclusive(
            DictationHotkey.ToggleVirtualKey,
            diagnosticSink);

        var segmentListener = new GlobalHotkeyListener(InPlaceSegmentHotkey.VirtualKey, diagnosticSink);

        var f9Listener = new GlobalHotkeyListener(ContinuousDictationHotkey.ToggleVirtualKey, diagnosticSink);

        var inPlaceOrchestrator = new InPlaceDictationOrchestrator(
            inPlaceSession,
            backend,
            injector,
            historyRepository,
            settingsStore,
            clock);

        var serviceCoordinator = new AsrServiceCoordinator(
            remoteApiProfileRepository,
            settingsStore,
            vocabularyRepository,
            secretProtector,
            serverManager,
            backend,
            modeBackend,
            profile => new RemoteOpenAiBackend(
                profile,
                secretProtector,
                new OpenAiCompatibleTranscriptionClient(
                    remoteHttpClientPool.GetClient(profile.ProxyUrl))),
            () => inPlaceOrchestrator.IsBusy
                || continuousSession.IsBusy,
            activityGate,
            () => transcribeClient.Refresh(serverManager.BaseUri));

        if (startupOptions.RuntimeMode == AppRuntimeMode.Standard)
        {
            await serviceCoordinator.InitializeAsync(cancellationToken);
            settings = await settingsStore.LoadAsync(cancellationToken);
        }

        var transcribeAttempt = 0;
        inPlaceOrchestrator.StatusChanged += status =>
        {
            var data = new Dictionary<string, string?>
            {
                ["message"] = status.Message,
                ["resultTextLength"] = status.ResultText?.Length.ToString(),
                ["errorMessage"] = status.ErrorMessage
            };
            if (status.State == InPlaceDictationState.Finishing)
            {
                data["transcribeAttempt"] = Interlocked.Increment(ref transcribeAttempt).ToString();
            }

            _ = diagnosticSink.WriteAsync(new DiagnosticEvent(
                0,
                DateTimeOffset.Now,
                "Dictation.StateChanged",
                status.State.ToString(),
                Environment.CurrentManagedThreadId,
                DiagnosticSnapshotCollector.Capture(),
                data));
        };

        var escapeCancelListener = new EscapeCancelListener(() =>
            continuousCoordinator.IsWindowOpen && continuousSession.IsRecordingActive
                || inPlaceOrchestrator.IsSessionOpen);



        void RunInPlace(Func<InPlaceDictationOrchestrator, Task> action, string errorContext)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await action(inPlaceOrchestrator);
                }
                catch (Exception ex)
                {
                    AppExceptionLogger.Report(ex, errorContext, showDialog: false);
                }
            });
        }

        overlayWindow.CloseRequested += () => RunInPlace(
            value => value.CancelOrDismissAsync(CancellationToken.None),
            "取消就地听写失败");
        overlayWindow.SubmitRequested += () => RunInPlace(
            value => value.ToggleAsync(CancellationToken.None),
            "完成就地听写失败");
        overlayWindow.SegmentTextChanged += inPlaceOrchestrator.UpdateSegmentText;

        var hotkeyRouter = new DictationHotkeyRouter(
            () => inPlaceOrchestrator.IsSessionOpen,
            () => inPlaceOrchestrator.State == InPlaceDictationState.Recording,
            () => continuousCoordinator.IsWindowOpen,
            () =>
            {
                if (inPlaceOrchestrator.State == InPlaceDictationState.Idle)
                {
                    injectionTargetCapture.Capture();
                    overlayWindow.LockPlacementToWindow(injectionTargetCapture.ForegroundWindow);
                }

                RunInPlace(
                    value => value.ToggleAsync(CancellationToken.None),
                    "右 Alt 就地听写处理失败");
            },
            () => RunInPlace(
                value => value.CommitSegmentBoundaryAsync(CancellationToken.None),
                "右 Ctrl 分段失败"),
            continuousCoordinator.HandleRightControl,
            continuousCoordinator.HandleF9);

        hotkeyListener.Triggered += hotkeyRouter.HandleRightAlt;
        segmentListener.Triggered += hotkeyRouter.HandleRightControl;
        f9Listener.Triggered += hotkeyRouter.HandleF9;

        escapeCancelListener.CancelRequested += () =>
        {
            if (continuousCoordinator.IsWindowOpen && continuousSession.IsRecordingActive)
            {
                continuousCoordinator.HandleEscape();
                return;
            }

            RunInPlace(
                value => value.CancelOrDismissAsync(CancellationToken.None),
                "Esc 取消就地听写失败");
        };



        if (settings.StartModelOnAppStartup
            && settings.ActiveRemoteApiProfileId is null
            && startupOptions.RuntimeMode == AppRuntimeMode.Standard)

        {

            _ = serverManager.EnsureStartedAsync(CancellationToken.None);

        }



        return new AppServices(

            database,

            settingsStore,

            vocabularyRepository,

            statsRepository,

            historyRepository,

            overlayWindow,

            hotkeyListener,

            segmentListener,

            f9Listener,

            escapeCancelListener,

            inPlaceOrchestrator,

            inPlaceSession,

            continuousCoordinator,

            continuousSession,

            injectionTargetCapture,

            transcribeClient,

            remoteHttpClientPool,

            remoteApiProfileRepository,

            backend,

            serviceCoordinator,

            activityGate,

            serverManager,

            diagnosticSink,

            startupOptions);

    }

    private static void ResetDemoDatabase(LessAsrPathLayout paths)
    {
        var demoRoot = Path.GetFullPath(LessAsrPaths.Demo.AppDataRoot);
        var productionRoot = Path.GetFullPath(LessAsrPaths.Production.AppDataRoot);
        var requestedRoot = Path.GetFullPath(paths.AppDataRoot);
        if (!string.Equals(requestedRoot, demoRoot, StringComparison.OrdinalIgnoreCase)
            || string.Equals(requestedRoot, productionRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("演示模式拒绝重置非演示数据目录。");
        }

        foreach (var path in new[]
                 {
                     paths.DatabasePath,
                     $"{paths.DatabasePath}-wal",
                     $"{paths.DatabasePath}-shm"
                 })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }



    public async ValueTask DisposeAsync()

    {

        HotkeyListener.Dispose();

        InPlaceSegmentHotkeyListener.Dispose();

        ContinuousDictationHotkeyListener.Dispose();

        EscapeCancelListener.Dispose();

        ContinuousDictationCoordinator.Dispose();

        await InPlaceDictationSession.TerminateAsync(CancellationToken.None);

        InPlaceOrchestrator.Dispose();

        TranscribeClient.Dispose();

        RemoteHttpClientPool.Dispose();

        await ServerManager.StopAsync(CancellationToken.None);

        await DiagnosticSink.DisposeAsync();

        await Database.DisposeAsync();

    }

}
