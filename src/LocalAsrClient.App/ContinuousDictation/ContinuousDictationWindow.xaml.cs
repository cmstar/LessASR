using System.Windows;
using System.Windows.Threading;
using Wpf.Ui.Controls;

namespace LocalAsrClient.App.ContinuousDictation;

public partial class ContinuousDictationWindow : FluentWindow
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
