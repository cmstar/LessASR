using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LocalAsrClient.App.Controls;

namespace LocalAsrClient.App.Tests.E2E;

public sealed class HelpLabelE2ETests
{
    [Fact]
    [Trait("Category", "UiE2E")]
    public void ToolTipDisplaysTheConfiguredHelpText()
    {
        const string expected = "代理服务器帮助文本";
        Exception? failure = null;
        using var completed = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            LocalAsrClient.App.App? application = null;
            Window? window = null;
            ToolTip? toolTip = null;
            try
            {
                application = new LocalAsrClient.App.App();
                application.InitializeComponent();
                var helpLabel = new HelpLabel
                {
                    Title = "代理服务器",
                    HelpText = expected,
                };
                window = new Window
                {
                    Content = helpLabel,
                    Width = 320,
                    Height = 120,
                    Left = -10000,
                    Top = -10000,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                };
                window.Show();
                window.UpdateLayout();

                var panel = Assert.IsType<StackPanel>(helpLabel.Content);
                var helpButton = Assert.IsType<Button>(panel.Children[1]);
                toolTip = Assert.IsType<ToolTip>(helpButton.ToolTip);
                toolTip.PlacementTarget = helpButton;
                toolTip.IsOpen = true;
                PumpDispatcher();

                var helpText = Assert.IsType<TextBlock>(toolTip.Content);
                Assert.Equal(expected, helpText.Text);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                if (toolTip is not null)
                {
                    toolTip.IsOpen = false;
                }

                window?.Close();
                application?.Shutdown();
                completed.Set();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(completed.Wait(TimeSpan.FromSeconds(5)), "Help tooltip test timed out.");
        thread.Join();
        if (failure is not null)
        {
            throw new Xunit.Sdk.XunitException(failure.ToString());
        }
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
