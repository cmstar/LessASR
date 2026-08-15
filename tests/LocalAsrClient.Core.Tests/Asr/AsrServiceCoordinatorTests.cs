using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Asr;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.Core.Tests.Asr;

public sealed class AsrServiceCoordinatorTests
{
    [Fact]
    public async Task ActivateRemoteAsync_StopsLocalBeforePersistingAndRouting()
    {
        var fixture = new Fixture();
        var profile = fixture.AddProfile("Office API", protectedApiKey: "protected");

        await fixture.Coordinator.ActivateRemoteAsync(profile.Id, CancellationToken.None);

        Assert.Equal(["stop", "save:remote"], fixture.Events);
        Assert.Equal(profile.Id, fixture.Settings.Settings.ActiveRemoteApiProfileId);
        Assert.Equal("Office API", fixture.Router.Name);
        Assert.Equal(1, fixture.RefreshLocalClientCalls);
    }

    [Fact]
    public async Task ActivateRemoteAsync_WhenLocalStopFails_DoesNotSwitchOrPersist()
    {
        var fixture = new Fixture();
        var profile = fixture.AddProfile("Office API");
        fixture.Manager.StopException = new InvalidOperationException("无法停止本地服务");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Coordinator.ActivateRemoteAsync(profile.Id, CancellationToken.None));

        Assert.Null(fixture.Settings.Settings.ActiveRemoteApiProfileId);
        Assert.Equal("本地 Whisper", fixture.Router.Name);
    }

    [Fact]
    public async Task ActivateLocalAsync_DoesNotStartLocalServer()
    {
        var fixture = new Fixture();
        var profile = fixture.AddProfile("Office API");
        fixture.Settings.Settings = fixture.Settings.Settings with { ActiveRemoteApiProfileId = profile.Id };
        fixture.Router.Replace(fixture.CreateBackend(profile));

        await fixture.Coordinator.ActivateLocalAsync(CancellationToken.None);

        Assert.Null(fixture.Settings.Settings.ActiveRemoteApiProfileId);
        Assert.Equal("本地 Whisper", fixture.Router.Name);
        Assert.Equal(0, fixture.Manager.EnsureStartedCalls);
    }

    [Theory]
    [InlineData(ApiKeyUpdateMode.Retain, "", "protected:old")]
    [InlineData(ApiKeyUpdateMode.Clear, "", null)]
    [InlineData(ApiKeyUpdateMode.Replace, "new", "protected:new")]
    public async Task UpdateRemoteAsync_AppliesExplicitApiKeySemantics(
        ApiKeyUpdateMode mode,
        string apiKey,
        string? expectedProtectedApiKey)
    {
        var fixture = new Fixture();
        var profile = fixture.AddProfile("Office API", "protected:old");

        await fixture.Coordinator.UpdateRemoteAsync(
            profile.Id,
            new RemoteApiProfileInput("Office API", "https://api.example/v1/audio/transcriptions", "whisper-1", true, apiKey),
            mode,
            CancellationToken.None);

        Assert.Equal(expectedProtectedApiKey, fixture.Repository.Get(profile.Id).ProtectedApiKey);
    }

    [Fact]
    public async Task CreateRemoteAsync_NormalizesAndPersistsItsProxyAddress()
    {
        var fixture = new Fixture();

        var profile = await fixture.Coordinator.CreateRemoteAsync(
            new RemoteApiProfileInput(
                "Proxy API",
                "https://api.example/v1/audio/transcriptions",
                "whisper-1",
                false,
                null,
                " socks5://127.0.0.1:1080 "),
            CancellationToken.None);

        Assert.Equal("socks5://127.0.0.1:1080/", profile.ProxyUrl);
        Assert.Equal(profile.ProxyUrl, fixture.Repository.Get(profile.Id).ProxyUrl);
    }

    [Fact]
    public async Task DeleteRemoteAsync_WhenProfileIsActive_RejectsDeletion()
    {
        var fixture = new Fixture();
        var profile = fixture.AddProfile("Office API");
        fixture.Settings.Settings = fixture.Settings.Settings with { ActiveRemoteApiProfileId = profile.Id };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Coordinator.DeleteRemoteAsync(profile.Id, CancellationToken.None));

        Assert.Contains("正在使用", error.Message, StringComparison.Ordinal);
        Assert.NotNull(fixture.Repository.Get(profile.Id));
    }

    [Fact]
    public async Task Mutations_WhenDictationBusy_AreRejected()
    {
        var fixture = new Fixture { IsBusy = true };
        var profile = fixture.AddProfile("Office API");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Coordinator.ActivateRemoteAsync(profile.Id, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Coordinator.UpdateRemoteAsync(
                profile.Id,
                new RemoteApiProfileInput(profile.Name, profile.Endpoint, profile.Model, false, null),
                ApiKeyUpdateMode.Retain,
                CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Coordinator.DeleteRemoteAsync(profile.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ActivateRemoteAsync_WhenDictationOwnsActivityGate_IsRejectedWithoutStoppingLocal()
    {
        var fixture = new Fixture();
        var profile = fixture.AddProfile("Office API");
        await using var dictation = Assert.IsType<AsrActivityLease>(
            await fixture.ActivityGate.TryEnterAsync(CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Coordinator.ActivateRemoteAsync(profile.Id, CancellationToken.None));

        Assert.Empty(fixture.Events);
        Assert.Null(fixture.Settings.Settings.ActiveRemoteApiProfileId);
    }

    [Fact]
    public async Task TestRemoteAsync_UsesSilentWavWithoutActivatingProfile()
    {
        var fixture = new Fixture();
        var profile = fixture.AddProfile("Office API");
        fixture.RemoteResult = string.Empty;

        var result = await fixture.Coordinator.TestRemoteAsync(profile.Id, CancellationToken.None);

        Assert.Equal(string.Empty, result.Text);
        Assert.Null(fixture.Settings.Settings.ActiveRemoteApiProfileId);
        Assert.Equal("本地 Whisper", fixture.Router.Name);
        Assert.NotNull(fixture.LastRemoteRequest);
        var audio = Assert.IsType<InMemoryAudioInput>(fixture.LastRemoteRequest!.Audio);
        Assert.Equal("wav", audio.Format);
        Assert.True(audio.Data.Length > 44);
    }

    [Fact]
    public async Task GetRemoteProfilesAsync_WhenSavedKeyCannotBeDecrypted_ReturnsUnavailableWithoutCiphertext()
    {
        var fixture = new Fixture();
        fixture.AddProfile("Office API", protectedApiKey: "broken-ciphertext");
        fixture.Protector.ThrowOnUnprotect = true;

        var profile = Assert.Single(
            await fixture.Coordinator.GetRemoteProfilesAsync(CancellationToken.None));

        Assert.Null(profile.ProtectedApiKey);
        Assert.Equal(ApiKeyAvailability.Unavailable, profile.ApiKeyAvailability);
    }

    [Fact]
    public async Task TestRemoteAsync_UsesCurrentPreferredLanguage()
    {
        var fixture = new Fixture();
        var profile = fixture.AddProfile("Office API");
        fixture.Settings.Settings = fixture.Settings.Settings with
        {
            PreferredTranscriptionLanguageId = "zh-Hans"
        };

        await fixture.Coordinator.TestRemoteAsync(profile.Id, CancellationToken.None);

        Assert.Equal("zh", fixture.LastRemoteRequest?.Language);
    }

    [Theory]
    [InlineData(true, "初音ミク, LessASR")]
    [InlineData(false, null)]
    public async Task TestRemoteAsync_UsesCurrentVocabularyOnlyWhenProfileEnablesIt(
        bool useVocabulary,
        string? expectedPrompt)
    {
        var fixture = new Fixture();
        var profile = fixture.AddProfile("Office API", useVocabulary: useVocabulary);
        var now = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);
        fixture.Vocabularies.ActiveProfile = new VocabularyProfile(
            Guid.NewGuid(),
            "编程",
            "LessASR\n初音ミク",
            true,
            now,
            now);

        await fixture.Coordinator.TestRemoteAsync(profile.Id, CancellationToken.None);

        Assert.Equal(expectedPrompt, fixture.LastRemoteRequest?.InitialPrompt);
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Manager = new FakeManager(Events);
            Local = new RecordingBackend("本地 Whisper", "local-model", _ => "local");
            Router = new SwitchableAsrBackend(Local);
            Settings.Events = Events;
            Coordinator = new AsrServiceCoordinator(
                Repository,
                Settings,
                Vocabularies,
                Protector,
                Manager,
                Router,
                Local,
                CreateBackend,
                () => IsBusy,
                ActivityGate,
                () => RefreshLocalClientCalls++);
        }

        public List<string> Events { get; } = [];
        public FakeRepository Repository { get; } = new();
        public FakeSettingsStore Settings { get; } = new();
        public FakeVocabularyRepository Vocabularies { get; } = new();
        public PrefixSecretProtector Protector { get; } = new();
        public FakeManager Manager { get; }
        public AsrActivityGate ActivityGate { get; } = new();
        public RecordingBackend Local { get; }
        public SwitchableAsrBackend Router { get; }
        public AsrServiceCoordinator Coordinator { get; }
        public bool IsBusy { get; set; }
        public int RefreshLocalClientCalls { get; private set; }
        public string RemoteResult { get; set; } = "ok";
        public AsrRequest? LastRemoteRequest { get; private set; }

        public RemoteApiProfile AddProfile(
            string name,
            string? protectedApiKey = null,
            bool useVocabulary = false)
        {
            var now = DateTimeOffset.UtcNow;
            var profile = new RemoteApiProfile(
                Guid.NewGuid(), name, "https://api.example/v1/audio/transcriptions", "whisper-1",
                protectedApiKey, useVocabulary, now, now);
            Repository.Profiles.Add(profile);
            return profile;
        }

        public IAsrBackend CreateBackend(RemoteApiProfile profile) =>
            new RecordingBackend(profile.Name, profile.Model, request =>
            {
                LastRemoteRequest = request;
                return RemoteResult;
            });

    }

    private sealed class RecordingBackend(string name, string modelId, Func<AsrRequest, string> result) : IAsrBackend
    {
        public string Name => name;
        public string ModelId => modelId;
        public AsrBackendStatus Status => AsrBackendStatus.Ready;
        public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<AsrResult> TranscribeAsync(AsrRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new AsrResult(result(request), null, null, null));
    }

    private sealed class FakeManager(List<string> events) : IWhisperServerManager
    {
        public event Action<WhisperServerStatus>? StatusChanged;
        public WhisperServerStatus Status => WhisperServerStatus.Stopped;
        public Uri BaseUri => new("http://127.0.0.1:8080");
        public Exception? StopException { get; set; }
        public int EnsureStartedCalls { get; private set; }
        public void UpdateOptions(WhisperServerOptions options) { }
        public Task EnsureStartedAsync(CancellationToken cancellationToken)
        {
            EnsureStartedCalls++;
            return Task.CompletedTask;
        }
        public Task StopAsync(CancellationToken cancellationToken)
        {
            events.Add("stop");
            return StopException is null ? Task.CompletedTask : Task.FromException(StopException);
        }
        public Task HealthCheckAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeSettingsStore : ISettingsStore
    {
        public List<string>? Events { get; set; }
        public AppSettings Settings { get; set; } = AppSettings.CreateDefault();
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            Settings = settings;
            Events?.Add(settings.ActiveRemoteApiProfileId is null ? "save:local" : "save:remote");
            return Task.CompletedTask;
        }
    }

    private sealed class PrefixSecretProtector : ISecretProtector
    {
        public bool ThrowOnUnprotect { get; set; }
        public string Protect(string plaintext) => $"protected:{plaintext}";
        public string Unprotect(string protectedValue) => ThrowOnUnprotect
            ? throw new InvalidOperationException("decrypt failed")
            : protectedValue.Replace("protected:", "", StringComparison.Ordinal);
    }

    private sealed class FakeVocabularyRepository : IVocabularyRepository
    {
        public VocabularyProfile? ActiveProfile { get; set; }

        public Task<VocabularyProfile?> GetActiveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ActiveProfile);

        public Task<IReadOnlyList<VocabularyProfile>> GetAllAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<VocabularyProfile> CreateAsync(string name, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateAsync(
            Guid id,
            string name,
            string entriesText,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SetActiveAsync(Guid? id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRepository : IRemoteApiProfileRepository
    {
        public List<RemoteApiProfile> Profiles { get; } = [];
        public RemoteApiProfile Get(Guid id) => Profiles.Single(profile => profile.Id == id);
        public Task<IReadOnlyList<RemoteApiProfile>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RemoteApiProfile>>(Profiles.ToArray());
        public Task<RemoteApiProfile?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Profiles.SingleOrDefault(profile => profile.Id == id));
        public Task<RemoteApiProfile> CreateAsync(string name, string endpoint, string model, string? protectedApiKey, bool useVocabulary, CancellationToken cancellationToken, string? proxyUrl = null)
        {
            var now = DateTimeOffset.UtcNow;
            var profile = new RemoteApiProfile(Guid.NewGuid(), name, endpoint, model, protectedApiKey, useVocabulary, now, now, proxyUrl);
            Profiles.Add(profile);
            return Task.FromResult(profile);
        }
        public Task UpdateAsync(Guid id, string name, string endpoint, string model, string? protectedApiKey, bool useVocabulary, CancellationToken cancellationToken, string? proxyUrl = null)
        {
            var index = Profiles.FindIndex(profile => profile.Id == id);
            Profiles[index] = Profiles[index] with
            {
                Name = name,
                Endpoint = endpoint,
                Model = model,
                ProtectedApiKey = protectedApiKey,
                UseVocabulary = useVocabulary,
                ProxyUrl = proxyUrl,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            return Task.CompletedTask;
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            Profiles.RemoveAll(profile => profile.Id == id);
            return Task.CompletedTask;
        }
    }
}
