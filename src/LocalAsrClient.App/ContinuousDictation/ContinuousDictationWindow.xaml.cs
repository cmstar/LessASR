using System.Windows;
using System.Windows.Threading;

namespace LocalAsrClient.App.ContinuousDictation;

public partial class ContinuousDictationWindow : Window
{
    public ContinuousDictationWindow(ContinuousDictationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.ScrollToBottomRequested += OnScrollToBottomRequested;
        Closed += (_, _) => viewModel.ScrollToBottomRequested -= OnScrollToBottomRequested;
    }

    private void OnScrollToBottomRequested()
    {
        Dispatcher.BeginInvoke(
            () => SegmentScrollViewer.ScrollToEnd(),
            DispatcherPriority.Loaded);
    }
}
