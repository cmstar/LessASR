using LocalAsrClient.App.Dialogs;

namespace LocalAsrClient.App.Tests.Dialogs;

public sealed class ConfirmationDialogOptionsTests
{
    [Fact]
    public void DefaultsKeepConfirmationNonDestructiveAndCancellationPreferred()
    {
        var options = new ConfirmationDialogOptions
        {
            Title = "确认操作",
            Heading = "是否继续？",
            Message = "请确认本次操作。"
        };

        Assert.Equal("确认", options.ConfirmText);
        Assert.Equal("取消", options.CancelText);
        Assert.Equal(ConfirmationDialogTone.Standard, options.Tone);
        Assert.False(options.IsConfirmDefault);
        Assert.False(options.HasPreview);
    }

    [Fact]
    public void DisplayedPreview_TrimsWhitespaceAndLimitsLongContent()
    {
        var options = new ConfirmationDialogOptions
        {
            Title = "确认操作",
            Heading = "是否继续？",
            Message = "请确认本次操作。",
            Preview = $"  {new string('文', 180)}  "
        };

        Assert.True(options.HasPreview);
        Assert.Equal(161, options.DisplayedPreview!.Length);
        Assert.EndsWith("…", options.DisplayedPreview);
    }
}
