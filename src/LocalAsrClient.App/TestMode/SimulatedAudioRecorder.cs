using LocalAsrClient.Core.Abstractions;
using LocalAsrClient.Core.Dictation;
using Timer = System.Threading.Timer;

namespace LocalAsrClient.App.TestMode;

public sealed class SimulatedAudioRecorder : IAudioRecorder, IAudioLevelSource
{
    private static readonly TimeSpan MinDuration = TimeSpan.FromMilliseconds(500);
    private DateTimeOffset _startedAt;
    private Timer? _levelTimer;
    private int _levelTick;

    public event Action<float>? AudioLevelChanged;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _startedAt = DateTimeOffset.Now;
        _levelTick = 0;
        _levelTimer = new Timer(
            _ =>
            {
                var tick = Interlocked.Increment(ref _levelTick);
                var level = 0.18f + (0.62f * MathF.Abs(MathF.Sin(tick * 0.58f)));
                AudioLevelChanged?.Invoke(level);
            },
            null,
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(50));
        return Task.CompletedTask;
    }

    public Task<RecordingResult> StopAsync(CancellationToken cancellationToken)
    {
        _levelTimer?.Dispose();
        _levelTimer = null;
        AudioLevelChanged?.Invoke(0);

        var duration = DateTimeOffset.Now - _startedAt;
        if (duration < MinDuration)
        {
            duration = MinDuration;
        }

        return Task.FromResult(new RecordingResult(
            CreateSilentWav(duration, sampleRate: 16000, channels: 1),
            duration,
            16000,
            1));
    }

    private static byte[] CreateSilentWav(TimeSpan duration, int sampleRate, int channels)
    {
        const short bitsPerSample = 16;
        var sampleCount = (int)Math.Max(1, duration.TotalSeconds * sampleRate);
        var blockAlign = channels * (bitsPerSample / 8);
        var byteRate = sampleRate * blockAlign;
        var dataSize = sampleCount * blockAlign;
        var buffer = new byte[44 + dataSize];

        buffer[0] = (byte)'R';
        buffer[1] = (byte)'I';
        buffer[2] = (byte)'F';
        buffer[3] = (byte)'F';
        WriteInt32(buffer, 4, 36 + dataSize);
        buffer[8] = (byte)'W';
        buffer[9] = (byte)'A';
        buffer[10] = (byte)'V';
        buffer[11] = (byte)'E';
        buffer[12] = (byte)'f';
        buffer[13] = (byte)'m';
        buffer[14] = (byte)'t';
        buffer[15] = (byte)' ';
        WriteInt32(buffer, 16, 16);
        WriteInt16(buffer, 20, 1);
        WriteInt16(buffer, 22, (short)channels);
        WriteInt32(buffer, 24, sampleRate);
        WriteInt32(buffer, 28, byteRate);
        WriteInt16(buffer, 32, (short)blockAlign);
        WriteInt16(buffer, 34, bitsPerSample);
        buffer[36] = (byte)'d';
        buffer[37] = (byte)'a';
        buffer[38] = (byte)'t';
        buffer[39] = (byte)'a';
        WriteInt32(buffer, 40, dataSize);

        return buffer;
    }

    private static void WriteInt16(byte[] buffer, int offset, short value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value & 0xFF);
        buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
        buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}
