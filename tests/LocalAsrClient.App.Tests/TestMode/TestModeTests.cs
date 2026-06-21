using LocalAsrClient.App.TestMode;

namespace LocalAsrClient.App.Tests.TestMode;

public sealed class TestModeTests
{
    [Fact]
    public async Task SimulatedAudioRecorderReturnsAtLeastHalfSecondDuration()
    {
        var recorder = new SimulatedAudioRecorder();
        await recorder.StartAsync(CancellationToken.None);
        var result = await recorder.StopAsync(CancellationToken.None);

        Assert.True(result.WavData.Length > 44);
        Assert.Equal(16000, result.SampleRate);
        Assert.Equal(1, result.Channels);
        Assert.True(result.Duration >= TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task TestAsrBackendReturnsConfiguredText()
    {
        var backend = new TestAsrBackend("LessASR 自动化测试文本");

        var result = await backend.TranscribeAsync(
            new LocalAsrClient.Core.Asr.AsrRequest(
                new LocalAsrClient.Core.Asr.InMemoryAudioInput([1, 2, 3], "wav", 16000, 1),
                "zh",
                new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.Equal("LessASR 自动化测试文本", result.Text);
    }

    [Theory]
    [InlineData(new[] { "--test-mode" }, true, true)]
    [InlineData(new[] { "--TEST-MODE" }, true, true)]
    [InlineData(new[] { "--diagnostics" }, false, true)]
    [InlineData(new[] { "--DIAGNOSTICS" }, false, true)]
    [InlineData(new[] { "--test-mode", "--diagnostics" }, true, true)]
    [InlineData(new string[0], false, false)]
    public void Resolve_EnablesFromStartupArgument(string[] args, bool expectedEnabled, bool expectedDiagnostics)
    {
        var options = TestModeOptions.Resolve(args);

        Assert.Equal(expectedEnabled, options.Enabled);
        Assert.Equal(expectedDiagnostics, options.DiagnosticsEnabled);
    }

    [Fact]
    public void Resolve_UsesDefaultAsrText()
    {
        var options = TestModeOptions.Resolve(["--test-mode"]);

        Assert.Equal(TestModeOptions.DefaultAsrText, options.AsrText);
    }
}
