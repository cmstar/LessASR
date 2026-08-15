using LocalAsrClient.App.ViewModels;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;
using System.Windows;
using System.Windows.Threading;

namespace LocalAsrClient.App.Tests.ViewModels;

public sealed class ServiceViewModelTests
{
    [Fact]
    public async Task LoadAsync_UsesPersistedRemoteSelectionAndBuildsOneCardPerProfile()
    {
        var profile = CreateProfile("Office API");
        var settings = new FakeSettingsStore(AppSettings.CreateDefault() with
        {
            ActiveRemoteApiProfileId = profile.Id
        });
        var coordinator = new FakeCoordinator(profile);
        var viewModel = CreateViewModel(settings, coordinator, new FakeManager());

        await viewModel.LoadAsync();

        Assert.False(viewModel.IsLocalActive);
        var card = Assert.Single(viewModel.RemoteProfiles);
        Assert.True(card.IsActive);
        Assert.Collection(
            viewModel.ModelProviders,
            local =>
            {
                Assert.True(local.IsLocal);
                Assert.False(local.CanDelete);
                Assert.False(local.IsSelected);
                Assert.Equal("本地 Whisper", local.Name);
            },
            remote =>
            {
                Assert.False(remote.IsLocal);
                Assert.True(remote.CanDelete);
                Assert.True(remote.IsSelected);
                Assert.Equal("Office API", remote.Name);
            });
        Assert.Same(card, viewModel.SelectedRemoteProfile);
        Assert.Equal("Office API · 远程 API", viewModel.ActiveServiceStatusText);
    }

    [Fact]
    public async Task StaleRemoteCardState_CannotCreateASecondCurrentModel()
    {
        var profile = CreateProfile("Office API");
        var viewModel = CreateViewModel(
            new FakeSettingsStore(AppSettings.CreateDefault()),
            new FakeCoordinator(profile),
            new FakeManager());
        await viewModel.LoadAsync();

        Assert.True(viewModel.IsLocalActive);

        Assert.Single(viewModel.RemoteProfiles).SetActive(true);

        var currentModel = Assert.Single(viewModel.ModelProviders.Where(item => item.IsActive));
        Assert.True(currentModel.IsLocal);
    }

    [Fact]
    public async Task AddRemoteProfile_CreatesUnsavedCardWithVocabularyDisabled()
    {
        var viewModel = CreateViewModel(
            new FakeSettingsStore(AppSettings.CreateDefault()),
            new FakeCoordinator(),
            new FakeManager());
        await viewModel.LoadAsync();

        viewModel.AddRemoteProfile();

        var card = Assert.Single(viewModel.RemoteProfiles);
        Assert.True(card.IsNew);
        Assert.False(card.UseVocabulary);
        Assert.Equal(2, viewModel.ModelProviders.Count);
        Assert.True(viewModel.ModelProviders[0].IsLocal);
        Assert.Same(card, viewModel.ModelProviders[1].RemoteProfile);
        Assert.Same(card, viewModel.SelectedRemoteProfile);
        Assert.False(viewModel.IsLocalSelected);
    }

    [Fact]
    public async Task DeleteSelectedRemoteProfile_ReturnsSelectionToFixedLocalModel()
    {
        var profile = CreateProfile("Office API");
        var viewModel = CreateViewModel(
            new FakeSettingsStore(AppSettings.CreateDefault()),
            new FakeCoordinator(profile),
            new FakeManager());
        await viewModel.LoadAsync();
        var remoteItem = Assert.Single(viewModel.ModelProviders.Where(item => !item.IsLocal));
        viewModel.SelectModelProvider(remoteItem);

        await Assert.Single(viewModel.RemoteProfiles).DeleteAsync();

        var localItem = Assert.Single(viewModel.ModelProviders);
        Assert.True(localItem.IsLocal);
        Assert.True(localItem.IsSelected);
        Assert.True(viewModel.IsLocalSelected);
        Assert.Null(viewModel.SelectedRemoteProfile);
    }

    [Fact]
    public async Task SaveLocalAsync_WhileRunning_RestartsAutomatically()
    {
        var settings = new FakeSettingsStore(AppSettings.CreateDefault());
        var manager = new FakeManager { MutableStatus = WhisperServerStatus.Ready };
        var viewModel = CreateViewModel(settings, new FakeCoordinator(), manager);
        await viewModel.LoadAsync();
        viewModel.WhisperServerPort = 18080;

        await viewModel.SaveLocalAsync();

        Assert.Equal(18080, settings.Settings.WhisperServerPort);
        Assert.Equal(WhisperServerStatus.Ready, manager.Status);
        Assert.False(manager.IsRestartRequired);
        Assert.Equal(["update", "stop", "start"], manager.Events);
    }

    [Fact]
    public async Task SaveLocalAsync_RefreshesClientBeforeRestarting()
    {
        var settings = new FakeSettingsStore(AppSettings.CreateDefault());
        var manager = new FakeManager { MutableStatus = WhisperServerStatus.Ready };
        var events = manager.Events;
        var viewModel = new ServiceViewModel(
            settings,
            new FakeCoordinator(),
            manager,
            () => events.Add("refresh"),
            () => false,
            _ => true);
        await viewModel.LoadAsync();
        viewModel.WhisperServerPort = 18080;

        await viewModel.SaveLocalAsync();

        Assert.Equal(["update", "stop", "refresh", "start"], events);
        Assert.False(manager.IsRestartRequired);
    }

    [Fact]
    public async Task LoadAsync_WithAutomaticThreadCount_LeavesTheEditorBlank()
    {
        var viewModel = CreateViewModel(
            new FakeSettingsStore(AppSettings.CreateDefault()),
            new FakeCoordinator(),
            new FakeManager());

        await viewModel.LoadAsync();

        Assert.Equal("", viewModel.WhisperServerThreadCountText);
        Assert.True(viewModel.RecommendedThreadCount > 0);
    }

    [Fact]
    public async Task SaveLocalAsync_WithBlankThreadCount_UsesTheAutomaticDefault()
    {
        var settings = new FakeSettingsStore(AppSettings.CreateDefault() with
        {
            WhisperServerThreadCount = 6
        });
        var viewModel = CreateViewModel(settings, new FakeCoordinator(), new FakeManager());
        await viewModel.LoadAsync();
        viewModel.WhisperServerThreadCountText = "";

        await viewModel.SaveLocalAsync();

        Assert.Null(settings.Settings.WhisperServerThreadCount);
    }

    [Fact]
    public async Task SaveLocalAsync_WithZeroThreadCount_ShowsValidationAndKeepsSavedValue()
    {
        var settings = new FakeSettingsStore(AppSettings.CreateDefault() with
        {
            WhisperServerThreadCount = 6
        });
        var viewModel = CreateViewModel(settings, new FakeCoordinator(), new FakeManager());
        await viewModel.LoadAsync();
        viewModel.WhisperServerThreadCountText = "0";

        await viewModel.SaveLocalAsync();

        Assert.Equal(6, settings.Settings.WhisperServerThreadCount);
        Assert.Contains("大于 0", viewModel.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaveLocalAsync_WhenDictationOwnsActivityGate_DoesNotChangeSettings()
    {
        var settings = new FakeSettingsStore(AppSettings.CreateDefault());
        var activityGate = new AsrActivityGate();
        var viewModel = CreateViewModel(
            settings,
            new FakeCoordinator(),
            new FakeManager(),
            activityGate);
        await viewModel.LoadAsync();
        viewModel.ModelPath = "draft-model.bin";
        await using var dictation = Assert.IsType<AsrActivityLease>(
            await activityGate.TryEnterAsync(CancellationToken.None));

        await viewModel.SaveLocalAsync();

        Assert.NotEqual("draft-model.bin", settings.Settings.ModelPath);
        Assert.Contains("听写", viewModel.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoteActivation_LocksTheWholeServicePageUntilItCompletes()
    {
        var first = CreateProfile("First API");
        var second = CreateProfile("Second API");
        var coordinator = new FakeCoordinator(first, second)
        {
            PauseActivation = true
        };
        var viewModel = CreateViewModel(
            new FakeSettingsStore(AppSettings.CreateDefault()),
            coordinator,
            new FakeManager());
        await viewModel.LoadAsync();

        var activation = viewModel.RemoteProfiles[0].ActivateAsync();
        await coordinator.ActivationStarted.Task;

        Assert.False(viewModel.CanMutate);
        Assert.All(viewModel.RemoteProfiles, profile => Assert.False(profile.CanMutate));

        coordinator.AllowActivation.SetResult();
        await activation;

        Assert.True(viewModel.CanMutate);
    }

    [Fact]
    public async Task SavingTheActiveRemoteName_NotifiesTheHomeStatusBinding()
    {
        var profile = CreateProfile("Old name");
        var viewModel = CreateViewModel(
            new FakeSettingsStore(AppSettings.CreateDefault() with
            {
                ActiveRemoteApiProfileId = profile.Id
            }),
            new FakeCoordinator(profile),
            new FakeManager());
        await viewModel.LoadAsync();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        var card = Assert.Single(viewModel.RemoteProfiles);
        card.Name = "New name";

        await card.SaveAsync("");

        Assert.Contains(nameof(ServiceViewModel.ActiveServiceStatusText), changed);
        Assert.Equal("New name · 远程 API", viewModel.ActiveServiceStatusText);
        Assert.Equal("New name", viewModel.ModelProviders[1].Name);
    }

    [Fact]
    public void BackgroundLocalStatusChange_NotifiesTheDisplayedStateOnTheUiDispatcher()
    {
        Exception? failure = null;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            Application? application = null;
            try
            {
                application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var manager = new FakeManager();
                var viewModel = CreateViewModel(
                    new FakeSettingsStore(AppSettings.CreateDefault()),
                    new FakeCoordinator(),
                    manager);
                var changed = new List<string?>();
                viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

                var background = new Thread(() => manager.RaiseStatus(WhisperServerStatus.Failed));
                background.Start();
                background.Join();
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(
                    DispatcherPriority.ApplicationIdle,
                    new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);

                Assert.Contains(nameof(ServiceViewModel.LocalServiceStateText), changed);
                Assert.Contains(nameof(ServiceViewModel.ActiveServiceStatusText), changed);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                application?.Shutdown();
                completed.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(completed.Wait(TimeSpan.FromSeconds(5)), "WPF Dispatcher test timed out.");
        thread.Join();
        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }

    private static ServiceViewModel CreateViewModel(
        ISettingsStore settings,
        IAsrServiceCoordinator coordinator,
        IWhisperServerManager manager,
        AsrActivityGate? activityGate = null) =>
        new(settings, coordinator, manager, () => { }, () => false, _ => true, activityGate);

    private static RemoteApiProfile CreateProfile(string name)
    {
        var now = DateTimeOffset.UtcNow;
        return new RemoteApiProfile(
            Guid.NewGuid(), name, "https://api.example/v1/audio/transcriptions", "whisper-1",
            null, false, now, now);
    }

    private sealed class FakeSettingsStore(AppSettings settings) : ISettingsStore
    {
        public AppSettings Settings { get; private set; } = settings;
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);
        public Task SaveAsync(AppSettings updated, CancellationToken cancellationToken)
        {
            Settings = updated;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCoordinator(params RemoteApiProfile[] profiles) : IAsrServiceCoordinator
    {
        private readonly List<RemoteApiProfile> _profiles = [.. profiles];
        public bool PauseActivation { get; init; }
        public TaskCompletionSource ActivationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowActivation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<RemoteApiProfile>> GetRemoteProfilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RemoteApiProfile>>(_profiles.ToArray());
        public Task<RemoteApiProfile> CreateRemoteAsync(RemoteApiProfileInput input, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            var profile = new RemoteApiProfile(Guid.NewGuid(), input.Name, input.Endpoint, input.Model, null, input.UseVocabulary, now, now);
            _profiles.Add(profile);
            return Task.FromResult(profile);
        }
        public Task UpdateRemoteAsync(Guid id, RemoteApiProfileInput input, ApiKeyUpdateMode apiKeyUpdateMode, CancellationToken cancellationToken)
        {
            var index = _profiles.FindIndex(profile => profile.Id == id);
            var existing = _profiles[index];
            _profiles[index] = existing with
            {
                Name = input.Name,
                Endpoint = input.Endpoint,
                Model = input.Model,
                UseVocabulary = input.UseVocabulary,
                ProtectedApiKey = apiKeyUpdateMode == ApiKeyUpdateMode.Clear
                    ? null
                    : existing.ProtectedApiKey
            };
            return Task.CompletedTask;
        }
        public Task DeleteRemoteAsync(Guid id, CancellationToken cancellationToken)
        {
            _profiles.RemoveAll(profile => profile.Id == id);
            return Task.CompletedTask;
        }
        public async Task ActivateRemoteAsync(Guid id, CancellationToken cancellationToken)
        {
            if (!PauseActivation)
            {
                return;
            }

            ActivationStarted.SetResult();
            await AllowActivation.Task;
        }
        public Task ActivateLocalAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<AsrResult> TestRemoteAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(new AsrResult(string.Empty, null, null, null));
    }

    private sealed class FakeManager : IWhisperServerManager
    {
        public event Action<WhisperServerStatus>? StatusChanged;
        public List<string> Events { get; } = [];
        public WhisperServerStatus MutableStatus { get; set; } = WhisperServerStatus.Stopped;
        public WhisperServerStatus Status => MutableStatus;
        public Uri BaseUri { get; private set; } = new("http://127.0.0.1:8080");
        public bool IsRestartRequired { get; private set; }
        public string ActiveModelPath => "model.bin";
        public void UpdateOptions(WhisperServerOptions options)
        {
            Events.Add("update");
            IsRestartRequired = Status == WhisperServerStatus.Ready;
            if (!IsRestartRequired)
            {
                BaseUri = options.BaseUri;
            }
        }
        public Task EnsureStartedAsync(CancellationToken cancellationToken)
        {
            Events.Add("start");
            MutableStatus = WhisperServerStatus.Ready;
            StatusChanged?.Invoke(MutableStatus);
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            Events.Add("stop");
            MutableStatus = WhisperServerStatus.Stopped;
            IsRestartRequired = false;
            StatusChanged?.Invoke(MutableStatus);
            return Task.CompletedTask;
        }
        public Task HealthCheckAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void RaiseStatus(WhisperServerStatus status)
        {
            MutableStatus = status;
            StatusChanged?.Invoke(status);
        }
    }
}
