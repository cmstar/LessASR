using System.Reflection;
using LocalAsrClient.App.Views;

namespace LocalAsrClient.App.Tests.DemoMode;

public sealed class DemoScreenshotExporterTests
{
    [Fact]
    public void ServiceView_ExposesItsPageScrollerForDeterministicScreenshotScrolling()
    {
        var pageScroller = typeof(ServiceView).GetField(
            "PageScrollViewer",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(pageScroller);
        Assert.Equal(typeof(System.Windows.Controls.ScrollViewer), pageScroller.FieldType);
    }
}
