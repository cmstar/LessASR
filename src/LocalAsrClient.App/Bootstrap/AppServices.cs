using System.IO;

using LocalAsrClient.App.Audio;

using LocalAsrClient.App.Asr;

using LocalAsrClient.App.Diagnostics;

using LocalAsrClient.App.Infrastructure;

using LocalAsrClient.App.Hotkeys;

using LocalAsrClient.App.Overlay;

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

        SqliteStatsRepository statsRepository,

        SqliteTextHistoryRepository historyRepository,

        DictationOverlayWindow overlayWindow,

        IHotkeyListener hotkeyListener,

        EscapeCancelListener escapeCancelListener,

        DictationOrchestrator orchestrator,

        InjectionTargetCapture injectionTargetCapture,

        ResilientWhisperServerClient transcribeClient,

        WhisperServerProcessManager serverManager,

        IDiagnosticEventSink diagnosticSink)

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

        TranscribeClient = transcribeClient;

        ServerManager = serverManager;

        DiagnosticSink = diagnosticSink;

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

    public ResilientWhisperServerClient TranscribeClient { get; }

    public WhisperServerProcessManager ServerManager { get; }

    public IDiagnosticEventSink DiagnosticSink { get; }



    public async Task ApplyServerOptionsFromSettingsAsync(CancellationToken cancellationToken = default)

    {

        var settings = await SettingsStore.LoadAsync(cancellationToken);

        var options = new WhisperServerOptions(

            settings.WhisperServerPath,

            settings.ModelPath,

            "127.0.0.1",

            settings.WhisperServerPort);

        ServerManager.UpdateOptions(options);

        var baseUri = options.BaseUri;
        var currentAuthority = TranscribeClient.BaseUri.GetLeftPart(UriPartial.Authority);
        var newAuthority = baseUri.GetLeftPart(UriPartial.Authority);
        if (!string.Equals(currentAuthority, newAuthority, StringComparison.OrdinalIgnoreCase))
        {
            TranscribeClient.Refresh(baseUri);
        }

    }

    public void RefreshTranscribeHttpClient()
    {
        TranscribeClient.Refresh(ServerManager.BaseUri);
    }



    public static async Task<AppServices> CreateAsync(
        string[]? startupArgs = null,
        CancellationToken cancellationToken = default)

    {

        Directory.CreateDirectory(LessAsrPaths.DataDirectory);

        var testMode = TestModeOptions.Resolve(startupArgs);
        IDiagnosticEventSink diagnosticSink = testMode.DiagnosticsEnabled
            ? JsonlDiagnosticEventSink.Create(LessAsrPaths.DiagnosticsDirectory)
            : NullDiagnosticEventSink.Instance;

        var database = await SqliteDatabase.OpenAsync(LessAsrPaths.DatabasePath, cancellationToken);

        var settingsStore = new SqliteSettingsStore(database);

        var settings = await settingsStore.LoadAsync(cancellationToken);



        var options = new WhisperServerOptions(

            settings.WhisperServerPath,

            settings.ModelPath,

            "127.0.0.1",

            settings.WhisperServerPort);

        var serverManager = new WhisperServerProcessManager(options, new AppFileLog());

        var transcribeClient = new ResilientWhisperServerClient(options.BaseUri, serverManager);

        IAsrBackend backend = testMode.Enabled
            ? new TestAsrBackend(testMode.AsrText)
            : new ManagedWhisperServerBackend(serverManager, transcribeClient);



        var statsRepository = new SqliteStatsRepository(database);

        var historyRepository = new SqliteTextHistoryRepository(database);

        IAudioRecorder recorder = testMode.Enabled
            ? new SimulatedAudioRecorder()
            : new NAudioMemoryRecorder();

        var injectionTargetCapture = new InjectionTargetCapture(diagnosticSink);

        var injector = new SendInputTextInjector(injectionTargetCapture, diagnosticSink);

        var overlayWindow = new DictationOverlayWindow(diagnosticSink);

        var hotkeyListener = new GlobalHotkeyListener(DictationHotkey.ToggleVirtualKey, diagnosticSink);

        var orchestrator = new DictationOrchestrator(

            recorder,

            backend,

            injector,

            statsRepository,

            historyRepository,

            settingsStore,

            new SystemClock());

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



        if (settings.StartModelOnAppStartup && !testMode.Enabled)

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

            transcribeClient,

            serverManager,

            diagnosticSink);

    }



    public async ValueTask DisposeAsync()

    {

        HotkeyListener.Dispose();

        EscapeCancelListener.Dispose();

        TranscribeClient.Dispose();

        await ServerManager.StopAsync(CancellationToken.None);

        await DiagnosticSink.DisposeAsync();

        await Database.DisposeAsync();

    }

}


