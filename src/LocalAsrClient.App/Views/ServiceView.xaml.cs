namespace LocalAsrClient.App.Views;

public partial class ServiceView : System.Windows.Controls.UserControl
{
    public ServiceView()
    {
        InitializeComponent();
    }

    internal void ScrollToPageOffset(double offset) =>
        PageScrollViewer.ScrollToVerticalOffset(offset);
}
