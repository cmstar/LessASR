using LocalAsrClient.App.TextInjection;

namespace LocalAsrClient.App.Tests.TextInjection;

public sealed class InjectionVerificationPolicyTests
{
    [Theory]
    [InlineData("Edit", true)]
    [InlineData("RichEdit50W", true)]
    [InlineData("Scintilla", true)]
    [InlineData("Chrome_RenderWidgetHostHWND", false)]
    [InlineData("", false)]
    public void IsVerificationRequired_ReflectsSupportedControlClasses(string className, bool expected)
    {
        var result = InjectionVerificationPolicy.IsVerificationRequired(className);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsInjectionVerified_SkipsReadBackForUnsupportedControlsWithTarget()
    {
        var result = InjectionVerificationPolicy.IsInjectionVerified(
            "Chrome_RenderWidgetHostHWND",
            readBack: null,
            injected: "LessASR");

        Assert.True(result);
    }

    [Theory]
    [InlineData("Hello LessASR", "LessASR", true)]
    [InlineData("Hello", "LessASR", false)]
    [InlineData(null, "LessASR", false)]
    public void IsInjectionVerified_RequiresReadBackForClassicControls(
        string? readBack,
        string injected,
        bool expected)
    {
        var result = InjectionVerificationPolicy.IsInjectionVerified("Edit", readBack, injected);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Scintilla", true)]
    [InlineData("Edit", false)]
    [InlineData("Chrome_RenderWidgetHostHWND", false)]
    public void TrustClipboardWithoutVerification_OnlyAppliesToScintilla(string className, bool expected)
    {
        var result = InjectionVerificationPolicy.TrustClipboardWithoutVerification(className);

        Assert.Equal(expected, result);
    }
}
