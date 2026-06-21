using LocalAsrClient.App.TextInjection;

namespace LocalAsrClient.App.Tests.TextInjection;

public sealed class FileExplorerInjectionPolicyTests
{
    [Theory]
    [InlineData("CabinetWClass", "Edit", true)]
    [InlineData("CabinetWClass", FileExplorerTargetResolver.InputSiteWindowClass, true)]
    [InlineData("CabinetWClass", "RichEdit50W", false)]
    [InlineData("Notepad", "Edit", false)]
    [InlineData("", "Edit", false)]
    public void ShouldUseClipboardOnly_ForExplorerEditAndInputSiteControls(
        string rootClassName,
        string editClassName,
        bool expected)
    {
        var result = FileExplorerInjectionPolicy.ShouldUseClipboardOnly(rootClassName, editClassName);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("CabinetWClass", true)]
    [InlineData("Notepad", false)]
    [InlineData("", false)]
    public void IsExplorerClassName_RecognizesExplorerWindows(string rootClassName, bool expected)
    {
        var result = FileExplorerInjectionPolicy.IsExplorerClassName(rootClassName);

        Assert.Equal(expected, result);
    }
}
