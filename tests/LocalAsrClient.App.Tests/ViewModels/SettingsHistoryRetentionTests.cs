using LocalAsrClient.App.ViewModels;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.Tests.ViewModels;

public sealed class SettingsHistoryRetentionTests
{
    [Theory]
    [InlineData(TranscriptRetentionPolicy.OneMonth, TranscriptRetentionPolicy.SevenDays, true)]
    [InlineData(TranscriptRetentionPolicy.SevenDays, TranscriptRetentionPolicy.OneDay, true)]
    [InlineData(TranscriptRetentionPolicy.OneDay, TranscriptRetentionPolicy.Disabled, true)]
    [InlineData(TranscriptRetentionPolicy.SevenDays, TranscriptRetentionPolicy.OneMonth, false)]
    [InlineData(TranscriptRetentionPolicy.Disabled, TranscriptRetentionPolicy.OneDay, false)]
    [InlineData(TranscriptRetentionPolicy.SevenDays, TranscriptRetentionPolicy.SevenDays, false)]
    public void IsShortening_RecognizesRetentionDirection(
        TranscriptRetentionPolicy previousPolicy,
        TranscriptRetentionPolicy newPolicy,
        bool expected)
    {
        Assert.Equal(expected, HistoryRetentionChange.IsShortening(previousPolicy, newPolicy));
    }

    [Fact]
    public async Task SaveAsync_WhenRetentionIsShortenedAndConfirmed_PrunesImmediately()
    {
        var settings = new StubSettingsStore(
            AppSettings.CreateDefault() with
            {
                TranscriptRetentionPolicy = TranscriptRetentionPolicy.OneMonth
            });
        var history = new StubHistoryRepository { PrunableCount = 12 };
        HistoryRetentionChange? confirmation = null;
        var viewModel = CreateViewModel(
            settings,
            history,
            change =>
            {
                confirmation = change;
                return true;
            });
        await viewModel.LoadAsync();
        viewModel.TranscriptRetentionPolicy = TranscriptRetentionPolicy.SevenDays;

        await viewModel.SaveAsync();

        Assert.Equal(TranscriptRetentionPolicy.SevenDays, settings.Settings.TranscriptRetentionPolicy);
        Assert.Equal(TranscriptRetentionPolicy.SevenDays, history.PrunedPolicy);
        Assert.Equal(12, confirmation?.DeleteCount);
        Assert.False(viewModel.HasUnsavedChanges);
    }

    [Fact]
    public async Task SaveAsync_WhenRetentionCleanupIsCancelled_DoesNotSaveOrPrune()
    {
        var settings = new StubSettingsStore(
            AppSettings.CreateDefault() with
            {
                TranscriptRetentionPolicy = TranscriptRetentionPolicy.OneMonth
            });
        var history = new StubHistoryRepository { PrunableCount = 12 };
        var viewModel = CreateViewModel(settings, history, _ => false);
        await viewModel.LoadAsync();
        viewModel.TranscriptRetentionPolicy = TranscriptRetentionPolicy.SevenDays;

        await viewModel.SaveAsync();

        Assert.Equal(TranscriptRetentionPolicy.OneMonth, settings.Settings.TranscriptRetentionPolicy);
        Assert.Null(history.PrunedPolicy);
        Assert.True(viewModel.HasUnsavedChanges);
    }

    [Fact]
    public async Task SaveAsync_WhenShortenedPolicyDeletesNothing_DoesNotShowConfirmation()
    {
        var settings = new StubSettingsStore(
            AppSettings.CreateDefault() with
            {
                TranscriptRetentionPolicy = TranscriptRetentionPolicy.OneMonth
            });
        var history = new StubHistoryRepository { PrunableCount = 0 };
        var confirmationShown = false;
        var viewModel = CreateViewModel(
            settings,
            history,
            _ =>
            {
                confirmationShown = true;
                return false;
            });
        await viewModel.LoadAsync();
        viewModel.TranscriptRetentionPolicy = TranscriptRetentionPolicy.SevenDays;

        await viewModel.SaveAsync();

        Assert.False(confirmationShown);
        Assert.Equal(TranscriptRetentionPolicy.SevenDays, settings.Settings.TranscriptRetentionPolicy);
        Assert.Equal(TranscriptRetentionPolicy.SevenDays, history.PrunedPolicy);
    }

    [Fact]
    public async Task SaveAsync_PreservesLocalServiceConfigurationOwnedByServicesPage()
    {
        var original = AppSettings.CreateDefault() with
        {
            ModelPath = "models/current.bin",
            WhisperServerPath = "tools/whisper-server.exe",
            WhisperServerPort = 18080,
            WhisperServerThreadCount = 6,
            StartModelOnAppStartup = true
        };
        var settings = new StubSettingsStore(original);
        var history = new StubHistoryRepository();
        var viewModel = CreateViewModel(settings, history, _ => true);
        await viewModel.LoadAsync();

        viewModel.MinimizeToTrayOnClose = false;
        await viewModel.SaveAsync();

        Assert.Equal(original.ModelPath, settings.Settings.ModelPath);
        Assert.Equal(original.WhisperServerPath, settings.Settings.WhisperServerPath);
        Assert.Equal(original.WhisperServerPort, settings.Settings.WhisperServerPort);
        Assert.Equal(original.WhisperServerThreadCount, settings.Settings.WhisperServerThreadCount);
        Assert.Equal(original.StartModelOnAppStartup, settings.Settings.StartModelOnAppStartup);
    }

    private static SettingsViewModel CreateViewModel(
        ISettingsStore settingsStore,
        ITextHistoryRepository historyRepository,
        Func<HistoryRetentionChange, bool> confirmHistoryCleanup)
    {
        return new SettingsViewModel(
            settingsStore,
            historyRepository,
            confirmHistoryCleanup);
    }

    private sealed class StubSettingsStore : ISettingsStore
    {
        public StubSettingsStore(AppSettings settings)
        {
            Settings = settings;
        }

        public AppSettings Settings { get; private set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Settings);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class StubHistoryRepository : ITextHistoryRepository
    {
        public int PrunableCount { get; init; }

        public TranscriptRetentionPolicy? PrunedPolicy { get; private set; }

        public Task AddAsync(TextHistoryEntry entry, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<TextHistoryEntry>> GetRecentAsync(
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TextHistoryEntry>>([]);

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<int> CountPrunableAsync(
            DateTimeOffset now,
            TranscriptRetentionPolicy policy,
            CancellationToken cancellationToken) =>
            Task.FromResult(PrunableCount);

        public Task PruneAsync(
            DateTimeOffset now,
            TranscriptRetentionPolicy policy,
            CancellationToken cancellationToken)
        {
            PrunedPolicy = policy;
            return Task.CompletedTask;
        }
    }
}
