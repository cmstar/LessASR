using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Tests.Asr;

public sealed class WhisperServerLaunchDetailsTests
{
    [Fact]
    public void FormatLaunch_IncludesArgumentsOnly()
    {
        var details = WhisperServerLaunchDetails.FormatLaunch("--host 127.0.0.1 --port 8080");

        Assert.Equal("命令行参数：--host 127.0.0.1 --port 8080", details);
    }

    [Fact]
    public void FormatFailure_IncludesExitCodeAndProcessOutputOnly()
    {
        var details = WhisperServerLaunchDetails.FormatFailure(
            0,
            "error: unknown argument --thread");

        Assert.Contains("退出码 0", details, StringComparison.Ordinal);
        Assert.DoesNotContain("可执行文件", details, StringComparison.Ordinal);
        Assert.DoesNotContain("命令行参数", details, StringComparison.Ordinal);
        Assert.Contains("error: unknown argument --thread", details, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatFailureSummary_IncludesTruncatedProcessOutput()
    {
        var output = new string('x', 3000);
        var summary = WhisperServerLaunchDetails.FormatFailureSummary(1, output);

        Assert.Contains("退出码 1", summary, StringComparison.Ordinal);
        Assert.Contains("进程输出：", summary, StringComparison.Ordinal);
        Assert.EndsWith("…", summary, StringComparison.Ordinal);
    }
}
