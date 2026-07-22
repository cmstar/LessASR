using LocalAsrClient.App.Tray;

namespace LocalAsrClient.App.Tests.Tray;

public sealed class TrayIconResourcesTests
{
    [Theory]
    [InlineData(true, TrayIconResources.DarkGlyphResourceName)]
    [InlineData(false, TrayIconResources.LightGlyphResourceName)]
    public void SelectResourceName_MatchesTaskbarContrast(
        bool systemUsesLightTheme,
        string expectedResourceName)
    {
        var resourceName = TrayIconResources.SelectResourceName(systemUsesLightTheme);

        Assert.Equal(expectedResourceName, resourceName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Load_ForEitherTaskbarTheme_ReturnsEmbeddedIcon(bool systemUsesLightTheme)
    {
        using var icon = TrayIconResources.Load(systemUsesLightTheme);

        Assert.True(icon.Width >= 16);
        Assert.True(icon.Height >= 16);
        Assert.NotEqual(IntPtr.Zero, icon.Handle);
    }
}
