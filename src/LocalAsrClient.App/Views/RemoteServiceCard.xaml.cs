using System.Windows;
using LocalAsrClient.App.ViewModels;

namespace LocalAsrClient.App.Views;

public partial class RemoteServiceCard : System.Windows.Controls.UserControl
{
    public RemoteServiceCard()
    {
        InitializeComponent();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RemoteServiceProfileViewModel viewModel)
        {
            return;
        }

        var key = ApiKeyBox.Password;
        if (await viewModel.SaveAsync(key))
        {
            ApiKeyBox.Clear();
        }
    }

    private async void ClearApiKey_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is RemoteServiceProfileViewModel viewModel)
        {
            await viewModel.ClearApiKeyAsync();
            ApiKeyBox.Clear();
        }
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is RemoteServiceProfileViewModel viewModel)
        {
            await viewModel.TestAsync();
        }
    }

    private async void Activate_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is RemoteServiceProfileViewModel viewModel)
        {
            await viewModel.ActivateAsync();
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is RemoteServiceProfileViewModel viewModel)
        {
            await viewModel.DeleteAsync();
        }
    }
}
