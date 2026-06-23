using System.Windows;

namespace LocalAsrClient.App.ContinuousDictation;

public partial class ContinuousDictationWindow : Window
{
    public ContinuousDictationWindow(ContinuousDictationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
