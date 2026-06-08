using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Tests.Asr;

public sealed class WhisperServerStatusFormatterTests
{
    [Theory]
    [InlineData(WhisperServerStatus.Stopped, "未启动")]
    [InlineData(WhisperServerStatus.Starting, "启动中")]
    [InlineData(WhisperServerStatus.Ready, "已就绪")]
    [InlineData(WhisperServerStatus.Transcribing, "识别中")]
    [InlineData(WhisperServerStatus.Failed, "启动失败")]
    public void ToDisplayText_ReturnsChineseLabel(WhisperServerStatus status, string expected)
    {
        Assert.Equal(expected, WhisperServerStatusFormatter.ToDisplayText(status));
    }
}
