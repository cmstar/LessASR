using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.Core.Abstractions;

public interface IAudioRecorder
{
    Task StartAsync(CancellationToken cancellationToken);
    Task<RecordingResult> StopAsync(CancellationToken cancellationToken);
}
