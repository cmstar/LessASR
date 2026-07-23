using LocalAsrClient.App.ViewModels;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.Tests.ViewModels;

public sealed class VocabularyViewModelTests
{
    [Fact]
    public async Task LoadAsync_ShowsSavedEntriesWithoutMarkingDirty()
    {
        var store = new StubSettingsStore
        {
            Settings = AppSettings.CreateDefault() with
            {
                VocabularyText = "LessASR\n大语言模型\n初音ミク"
            }
        };
        var viewModel = new VocabularyViewModel(store);

        await viewModel.LoadAsync();

        Assert.Equal("LessASR\n大语言模型\n初音ミク", viewModel.VocabularyText);
        Assert.Equal("3 / 100", viewModel.EntryCountText);
        Assert.False(viewModel.HasUnsavedChanges);
        Assert.False(viewModel.CanSave);
        Assert.Equal("已保存", viewModel.SaveButtonText);
    }

    [Fact]
    public async Task Editing_InvalidEntry_ShowsLineErrorAndDisablesSave()
    {
        var viewModel = new VocabularyViewModel(new StubSettingsStore());
        await viewModel.LoadAsync();

        viewModel.VocabularyText = $"正常\n{new string('词', 31)}";

        Assert.Equal("第 2 行超过 30 个字符。", viewModel.ValidationError);
        Assert.True(viewModel.HasUnsavedChanges);
        Assert.False(viewModel.CanSave);
    }

    [Fact]
    public async Task SaveAsync_NormalizesTextAndPreservesOtherLatestSettings()
    {
        var store = new StubSettingsStore
        {
            Settings = AppSettings.CreateDefault() with { ModelPath = "first-model.bin" }
        };
        var viewModel = new VocabularyViewModel(store);
        await viewModel.LoadAsync();
        viewModel.VocabularyText = "  LessASR \r\n\r\n初音ミク\nLessASR\nKubernetes ";
        store.Settings = store.Settings with { ModelPath = "latest-model.bin" };

        await viewModel.SaveAsync();

        Assert.Equal("LessASR\n初音ミク\nKubernetes", store.Settings.VocabularyText);
        Assert.Equal("latest-model.bin", store.Settings.ModelPath);
        Assert.Equal(store.Settings.VocabularyText, viewModel.VocabularyText);
        Assert.False(viewModel.HasUnsavedChanges);
        Assert.False(viewModel.CanSave);
        Assert.StartsWith("上次保存：", viewModel.LastSavedAtText, StringComparison.Ordinal);
    }

    private sealed class StubSettingsStore : ISettingsStore
    {
        public AppSettings Settings { get; set; } = AppSettings.CreateDefault();

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Settings);
        }

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }
}
