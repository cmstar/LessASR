using System.IO;
using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.App.TestMode;

public sealed class TestAudioRecorder : IAudioRecorder
{
    private readonly string _wavPath;
    private DateTimeOffset _startedAt;

    public TestAudioRecorder(string wavPath)
    {
        _wavPath = wavPath;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_wavPath))
        {
            throw new FileNotFoundException("测试音频不存在。", _wavPath);
        }

        _startedAt = DateTimeOffset.Now;
        return Task.CompletedTask;
    }

    public Task<RecordingResult> StopAsync(CancellationToken cancellationToken)
    {
        var bytes = File.ReadAllBytes(_wavPath);
        var duration = DateTimeOffset.Now - _startedAt;
        if (duration < TimeSpan.FromSeconds(4))
        {
            duration = TimeSpan.FromSeconds(4);
        }

        return Task.FromResult(new RecordingResult(bytes, duration, 16000, 1));
    }
}
