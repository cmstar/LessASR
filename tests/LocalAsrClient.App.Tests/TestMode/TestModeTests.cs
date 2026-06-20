using LocalAsrClient.App.TestMode;

namespace LocalAsrClient.App.Tests.TestMode;

public sealed class TestModeTests
{
    [Fact]
    public async Task TestAudioRecorderReturnsConfiguredWavFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "test-sound.wav");
        Assert.True(File.Exists(path), $"Missing copied test audio: {path}");

        var recorder = new TestAudioRecorder(path);
        await recorder.StartAsync(CancellationToken.None);
        var result = await recorder.StopAsync(CancellationToken.None);

        Assert.True(result.WavData.Length > 44);
        Assert.Equal(16000, result.SampleRate);
        Assert.Equal(1, result.Channels);
        Assert.True(result.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task TestAsrBackendReturnsConfiguredText()
    {
        var backend = new TestAsrBackend("LessASR 自动化测试文本");

        var result = await backend.TranscribeAsync(
            new LocalAsrClient.Core.Asr.AsrRequest(
                new LocalAsrClient.Core.Asr.InMemoryAudioInput([1, 2, 3], "wav", 16000, 1),
                "zh",
                null,
                new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.Equal("LessASR 自动化测试文本", result.Text);
    }
}
