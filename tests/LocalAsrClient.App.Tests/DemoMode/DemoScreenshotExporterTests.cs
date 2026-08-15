using System.Reflection;
using LocalAsrClient.App.Views;

namespace LocalAsrClient.App.Tests.DemoMode;

public sealed class DemoScreenshotExporterTests
{
    [Fact]
    public void ModelView_ExposesItsRootForDeterministicScreenshotRendering()
    {
        var captureRoot = typeof(ServiceView).GetField(
            "ScreenshotServiceRoot",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotNull(captureRoot);
        Assert.Equal(typeof(System.Windows.Controls.Grid), captureRoot.FieldType);
    }
}
