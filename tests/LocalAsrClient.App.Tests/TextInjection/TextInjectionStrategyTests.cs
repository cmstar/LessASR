using LocalAsrClient.App.TextInjection;

namespace LocalAsrClient.App.Tests.TextInjection;

public sealed class TextInjectionStrategyTests
{
    [Theory]
    [InlineData("Edit")]
    [InlineData("RichEdit50W")]
    [InlineData("RichEditD2DPT")]
    [InlineData("ThunderRT6TextBox")]
    public void SelectsReplaceSelectionForClassicEditControls(string className)
    {
        var strategy = TextInjectionStrategy.Select(className);

        Assert.Equal(TextInjectionMethod.ReplaceSelectionMessage, strategy);
    }

    [Fact]
    public void SelectsScintillaMessageForScintillaControls()
    {
        var strategy = TextInjectionStrategy.Select("Scintilla");

        Assert.Equal(TextInjectionMethod.ScintillaReplaceSelectionMessage, strategy);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Chrome_RenderWidgetHostHWND")]
    [InlineData("Windows.UI.Core.CoreWindow")]
    public void SelectsClipboardPasteForUnknownOrModernControls(string className)
    {
        var strategy = TextInjectionStrategy.Select(className);

        Assert.Equal(TextInjectionMethod.ClipboardPaste, strategy);
    }

    [Theory]
    [InlineData("Edit", true)]
    [InlineData("RichEditD2DPT", true)]
    [InlineData("Scintilla", false)]
    [InlineData("Chrome_RenderWidgetHostHWND", false)]
    public void TrustDirectWithoutVerification_OnlyAppliesToRichEditAndEdit(string className, bool expected)
    {
        var result = TextInjectionStrategy.TrustDirectWithoutVerification(className);

        Assert.Equal(expected, result);
    }
}
