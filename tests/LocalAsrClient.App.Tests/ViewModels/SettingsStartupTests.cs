using LocalAsrClient.App.ViewModels;
using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.Tests.ViewModels;

public sealed class SettingsStartupTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StartupSetting_LoadsSavesAndRemainsIndependentOfCloseBehavior(
        bool initiallyMinimized,
        bool minimizeOnClose)
    {
        await using var database = await SqliteDatabase.CreateInMemoryAsync();
        var store = new SqliteSettingsStore(database);
        var history = new SqliteTextHistoryRepository(database);
        await store.SaveAsync(AppSettings.CreateDefault() with
        {
            MinimizeToTrayOnStartup = initiallyMinimized,
            MinimizeToTrayOnClose = minimizeOnClose
        }, CancellationToken.None);
        var viewModel = new SettingsViewModel(store, history);

        await viewModel.LoadAsync();

        Assert.Equal(initiallyMinimized, viewModel.MinimizeToTrayOnStartup);
        Assert.False(viewModel.HasUnsavedChanges);

        // Another page can change the active service while settings are being edited.
        var profileId = Guid.NewGuid();
        await store.UpdateAsync(settings => settings with
        {
            ActiveRemoteApiProfileId = profileId,
            StartModelOnAppStartup = true
        }, CancellationToken.None);
        viewModel.MinimizeToTrayOnStartup = !initiallyMinimized;

        Assert.True(viewModel.CanSave);
        Assert.Equal(initiallyMinimized, (await store.LoadAsync(CancellationToken.None)).MinimizeToTrayOnStartup);

        await viewModel.SaveAsync();

        var loaded = await store.LoadAsync(CancellationToken.None);
        Assert.Equal(!initiallyMinimized, loaded.MinimizeToTrayOnStartup);
        Assert.Equal(minimizeOnClose, loaded.MinimizeToTrayOnClose);
        Assert.Equal(profileId, loaded.ActiveRemoteApiProfileId);
        Assert.True(loaded.StartModelOnAppStartup);
        Assert.False(viewModel.HasUnsavedChanges);
        var reopenedViewModel = new SettingsViewModel(store, history);
        await reopenedViewModel.LoadAsync();
        Assert.Equal(!initiallyMinimized, reopenedViewModel.MinimizeToTrayOnStartup);
        Assert.False(reopenedViewModel.HasUnsavedChanges);
    }
}
