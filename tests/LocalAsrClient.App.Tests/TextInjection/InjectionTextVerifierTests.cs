using LocalAsrClient.App.TextInjection;

namespace LocalAsrClient.App.Tests.TextInjection;

public sealed class InjectionTextVerifierTests
{
    [Theory]
    [InlineData("Hello LessASR", "LessASR", true)]
    [InlineData("前缀 LessASR 后缀", "LessASR", true)]
    [InlineData("LessASR", "LessASR", true)]
    [InlineData("", "LessASR", false)]
    [InlineData(null, "LessASR", false)]
    [InlineData("Hello", "LessASR", false)]
    [InlineData("LessAS", "LessASR", false)]
    public void ContainsInjectedText_MatchesExpected(string? readBack, string injected, bool expected)
    {
        var result = InjectionTextVerifier.ContainsInjectedText(readBack, injected);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Edit", true)]
    [InlineData("RichEdit50W", true)]
    [InlineData("RichEditD2DPT", true)]
    [InlineData("Scintilla", true)]
    [InlineData("Chrome_RenderWidgetHostHWND", false)]
    [InlineData("", false)]
    public void CanReadBackText_ReflectsSupportedControlClasses(string className, bool expected)
    {
        var result = InjectionTextVerifier.CanReadBackText(className);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("RichEditD2DPT", true)]
    [InlineData("RichEdit50W", true)]
    [InlineData("RICHEDIT60W", true)]
    [InlineData("Edit", false)]
    [InlineData("Scintilla", false)]
    [InlineData("", false)]
    public void IsRichEditClassName_DistinguishesRichEditFromClassicEdit(string className, bool expected)
    {
        var result = TextInjectionStrategy.IsRichEditClassName(className);

        Assert.Equal(expected, result);
    }
}
