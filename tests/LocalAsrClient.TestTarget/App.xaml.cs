using System.Windows;

namespace LocalAsrClient.TestTarget;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        TestTargetStartupOptions.Parse(e.Args);
        base.OnStartup(e);
    }
}
