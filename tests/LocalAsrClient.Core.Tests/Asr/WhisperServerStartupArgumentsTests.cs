using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Tests.Asr;

public sealed class WhisperServerStartupArgumentsTests
{
    [Fact]
    public void Build_IncludesHostPortAndModelPath()
    {
        var options = new WhisperServerOptions(
            ServerExecutablePath: @"C:\tools\whisper-server.exe",
            ModelPath: @"C:\models\ggml-base.bin",
            Host: "127.0.0.1",
            Port: 8080);

        var arguments = WhisperServerStartupArguments.Build(options);

        Assert.Contains("--host 127.0.0.1", arguments, StringComparison.Ordinal);
        Assert.Contains("--port 8080", arguments, StringComparison.Ordinal);
        Assert.Contains("--max-context 0", arguments, StringComparison.Ordinal);
        Assert.Contains("-m \"C:\\models\\ggml-base.bin\"", arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("-nc", arguments, StringComparison.Ordinal);
    }
}
