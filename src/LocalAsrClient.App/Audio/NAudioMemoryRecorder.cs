using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Dictation;
using NAudio.Wave;

namespace LocalAsrClient.App.Audio;

public sealed class NAudioMemoryRecorder : IAudioRecorder, IAudioLevelSource, IDisposable
{
    private WaveInEvent? _waveIn;
    private System.IO.MemoryStream? _buffer;
    private WaveFileWriter? _writer;
    private DateTimeOffset _startedAt;

    public event Action<float>? AudioLevelChanged;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _buffer = new System.IO.MemoryStream();
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 50
        };
        _writer = new WaveFileWriter(_buffer, _waveIn.WaveFormat);
        _waveIn.DataAvailable += OnDataAvailable;
        _startedAt = DateTimeOffset.Now;
        _waveIn.StartRecording();
        return Task.CompletedTask;
    }

    public Task<RecordingResult> StopAsync(CancellationToken cancellationToken)
    {
        if (_waveIn is null || _writer is null || _buffer is null)
        {
            throw new InvalidOperationException("录音尚未开始。");
        }

        _waveIn.StopRecording();
        _waveIn.DataAvailable -= OnDataAvailable;
        _writer.Flush();
        _writer.Dispose();
        var data = _buffer.ToArray();
        var duration = DateTimeOffset.Now - _startedAt;

        _waveIn.Dispose();
        _buffer.Dispose();
        _waveIn = null;
        _writer = null;
        _buffer = null;
        AudioLevelChanged?.Invoke(0);

        return Task.FromResult(new RecordingResult(data, duration, 16000, 1));
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        AudioLevelChanged?.Invoke(
            AudioLevelCalculator.CalculateNormalizedRms(e.Buffer.AsSpan(0, e.BytesRecorded)));
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _waveIn?.Dispose();
        _buffer?.Dispose();
    }
}
