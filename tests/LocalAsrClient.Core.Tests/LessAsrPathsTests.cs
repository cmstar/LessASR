using LocalAsrClient.Core;

namespace LocalAsrClient.Core.Tests;

public sealed class LessAsrPathsTests
{
    [Fact]
    public void AppDataRoot_IsUnderUserProfile()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(Path.Combine(userProfile, ".lessasr"), LessAsrPaths.AppDataRoot);
    }

    [Fact]
    public void FixedSubdirectories_AreUnderAppDataRoot()
    {
        Assert.Equal(Path.Combine(LessAsrPaths.AppDataRoot, "data"), LessAsrPaths.DataDirectory);
        Assert.Equal(Path.Combine(LessAsrPaths.AppDataRoot, "logs"), LessAsrPaths.LogsDirectory);
        Assert.Equal(Path.Combine(LessAsrPaths.AppDataRoot, "diagnostics"), LessAsrPaths.DiagnosticsDirectory);
        Assert.Equal(Path.Combine(LessAsrPaths.DataDirectory, "client.db"), LessAsrPaths.DatabasePath);
    }

    [Fact]
    public void ProductName_IsLessAsr()
    {
        Assert.Equal("LessASR", LessAsrPaths.ProductName);
    }

    [Fact]
    public void DemoLayout_IsIsolatedFromProductionAndStoredUnderTemp()
    {
        var demo = LessAsrPaths.Demo;
        var production = LessAsrPaths.Production;
        var tempRoot = Path.GetFullPath(Path.GetTempPath());

        Assert.NotEqual(
            Path.GetFullPath(production.AppDataRoot),
            Path.GetFullPath(demo.AppDataRoot));
        Assert.StartsWith(
            tempRoot,
            Path.GetFullPath(demo.AppDataRoot),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(demo.AppDataRoot, "data", "client.db"), demo.DatabasePath);
        Assert.Equal(Path.Combine(demo.AppDataRoot, "logs"), demo.LogsDirectory);
    }
}
