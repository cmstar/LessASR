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

    [Fact]
    public void NavigatesToVocabularySection()
    {
        var viewModel = new MainNavigationViewModel();

        viewModel.NavigateCommand.Execute(MainSection.Vocabulary);

        Assert.Equal(MainSection.Vocabulary, viewModel.SelectedSection);
        Assert.True(viewModel.IsVocabularySelected);
        Assert.False(viewModel.IsServicesSelected);
        Assert.False(viewModel.IsSettingsSelected);
    }

    [Fact]
    public void NavigatesToServicesSection()
    {
        var viewModel = new MainNavigationViewModel();

        viewModel.NavigateCommand.Execute(MainSection.Services);

        Assert.Equal(MainSection.Services, viewModel.SelectedSection);
        Assert.True(viewModel.IsServicesSelected);
        Assert.False(viewModel.IsSettingsSelected);
    }
}
