using System.IO;
using System.Net.Http;

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

        GlobalHotkeyListener continuousDictationHotkeyListener,

        EscapeCancelListener escapeCancelListener,

        DictationOrchestrator orchestrator,

        ContinuousDictationCoordinator continuousDictationCoordinator,

        ContinuousDictationSession continuousDictationSession,

        InjectionTargetCapture injectionTargetCapture,

        ResilientWhisperServerClient transcribeClient,

        HttpClient remoteHttpClient,

        SqliteRemoteApiProfileRepository remoteApiProfileRepository,

        SwitchableAsrBackend backendRouter,

        AsrServiceCoordinator serviceCoordinator,

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

        ContinuousDictationHotkeyListener = continuousDictationHotkeyListener;

        EscapeCancelListener = escapeCancelListener;

        Orchestrator = orchestrator;

        ContinuousDictationCoordinator = continuousDictationCoordinator;

        ContinuousDictationSession = continuousDictationSession;

        InjectionTargetCapture = injectionTargetCapture;

        TranscribeClient = transcribeClient;

        RemoteHttpClient = remoteHttpClient;

        RemoteApiProfileRepository = remoteApiProfileRepository;

        BackendRouter = backendRouter;

        ServiceCoordinator = serviceCoordinator;

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

    public GlobalHotkeyListener ContinuousDictationHotkeyListener { get; }

    public EscapeCancelListener EscapeCancelListener { get; }

    public DictationOrchestrator Orchestrator { get; }

    public ContinuousDictationCoordinator ContinuousDictationCoordinator { get; }

    public ContinuousDictationSession ContinuousDictationSession { get; }

    public InjectionTargetCapture InjectionTargetCapture { get; }

    public ResilientWhisperServerClient TranscribeClient { get; }

    public HttpClient RemoteHttpClient { get; }

    public SqliteRemoteApiProfileRepository RemoteApiProfileRepository { get; }

    public SwitchableAsrBackend BackendRouter { get; }

    public AsrServiceCoordinator ServiceCoordinator { get; }

    public WhisperServerProcessManager ServerManager { get; }

    public IDiagnosticEventSink DiagnosticSink { get; }

    public AppStartupOptions StartupOptions { get; }

    public LessAsrPathLayout Paths => StartupOptions.Paths;

    public bool IsDemoMode => StartupOptions.IsDemoMode;

    public bool IsDictationBusy => Orchestrator.State is DictationState.Recording
        or DictationState.Transcribing
        or DictationState.Injecting
        or DictationState.EnsuringModelReady
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

        var remoteApiProfileRepository = new SqliteRemoteApiProfileRepository(database);
        var secretProtector = new DpapiSecretProtector();
        var remoteHttpClient = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            MaxConnectionsPerServer = 1
        })
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
        var remoteTranscriptionClient = new OpenAiCompatibleTranscriptionClient(remoteHttpClient);



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

        var continuousSession = new ContinuousDictationSession(continuousRecorder, transcriptionPipeline);

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

        var hotkeyListener = new GlobalHotkeyListener(DictationHotkey.ToggleVirtualKey, diagnosticSink);

        var f9Listener = new GlobalHotkeyListener(ContinuousDictationHotkey.ToggleVirtualKey, diagnosticSink);

        var orchestrator = new DictationOrchestrator(

            singleRecorder,

            backend,

            injector,

            statsRepository,

            historyRepository,

            settingsStore,

            vocabularyRepository,

            clock,

            new TranscriptionScriptPostProcessor(settingsStore));

        var serviceCoordinator = new AsrServiceCoordinator(
            remoteApiProfileRepository,
            settingsStore,
            secretProtector,
            serverManager,
            backend,
            modeBackend,
            profile => new RemoteOpenAiBackend(profile, secretProtector, remoteTranscriptionClient),
            () => orchestrator.State is DictationState.Recording
                or DictationState.Transcribing
                or DictationState.Injecting
                or DictationState.EnsuringModelReady
                || continuousSession.IsBusy);

        if (startupOptions.RuntimeMode == AppRuntimeMode.Standard)
        {
            await serviceCoordinator.InitializeAsync(cancellationToken);
            settings = await settingsStore.LoadAsync(cancellationToken);
        }

        var transcribeAttempt = 0;
        orchestrator.StatusChanged += status =>
        {
            var data = new Dictionary<string, string?>
            {
                ["message"] = status.Message,
                ["resultTextLength"] = status.ResultText?.Length.ToString(),
                ["errorMessage"] = status.ErrorMessage
            };
            if (status.State == DictationState.Transcribing)
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
                || orchestrator.State == DictationState.Recording);



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
            if (continuousCoordinator.IsWindowOpen)
            {
                continuousCoordinator.HandleRightControl();
                return;
            }

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

        f9Listener.Triggered += () => continuousCoordinator.HandleF9();



        escapeCancelListener.CancelRequested += () =>

        {

            if (continuousCoordinator.IsWindowOpen && continuousSession.IsRecordingActive)
            {
                continuousCoordinator.HandleEscape();
                return;
            }

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

            f9Listener,

            escapeCancelListener,

            orchestrator,

            continuousCoordinator,

            continuousSession,

            injectionTargetCapture,

            transcribeClient,

            remoteHttpClient,

            remoteApiProfileRepository,

            backend,

            serviceCoordinator,

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

        ContinuousDictationHotkeyListener.Dispose();

        EscapeCancelListener.Dispose();

        ContinuousDictationCoordinator.Dispose();

        TranscribeClient.Dispose();

        RemoteHttpClient.Dispose();

        await ServerManager.StopAsync(CancellationToken.None);

        await DiagnosticSink.DisposeAsync();

        await Database.DisposeAsync();

    }

}
