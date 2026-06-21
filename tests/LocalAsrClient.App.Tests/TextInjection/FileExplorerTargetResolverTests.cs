using LocalAsrClient.App.TextInjection;

namespace LocalAsrClient.App.Tests.TextInjection;

public sealed class FileExplorerTargetResolverTests
{
    [Theory]
    [InlineData(FileExplorerTargetResolver.InputSiteWindowClass, true)]
    [InlineData("Edit", false)]
    [InlineData("DirectUIHWND", false)]
    public void IsExplorerInputSiteClassName_RecognizesWin11ExplorerInputHosts(string className, bool expected)
    {
        var result = FileExplorerTargetResolver.IsExplorerInputSiteClassName(className);

        Assert.Equal(expected, result);
    }
}
