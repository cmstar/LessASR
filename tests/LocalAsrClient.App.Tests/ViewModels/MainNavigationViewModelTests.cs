using LocalAsrClient.App.ViewModels;

namespace LocalAsrClient.App.Tests.ViewModels;

public sealed class MainNavigationViewModelTests
{
    [Fact]
    public void StartsOnHomeAndNavigatesToRequestedSection()
    {
        var viewModel = new MainNavigationViewModel();

        Assert.Equal(MainSection.Home, viewModel.SelectedSection);

        viewModel.NavigateCommand.Execute(MainSection.Settings);

        Assert.Equal(MainSection.Settings, viewModel.SelectedSection);
        Assert.True(viewModel.IsSettingsSelected);
        Assert.False(viewModel.IsHomeSelected);
    }
}
