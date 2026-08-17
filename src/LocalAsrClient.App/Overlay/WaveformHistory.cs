namespace LocalAsrClient.App.Overlay;

internal sealed class WaveformHistory
{
    public const int DefaultBarCount = 96;
    private readonly float[] _samples;

    public WaveformHistory(int barCount = DefaultBarCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(barCount);
        _samples = new float[barCount];
    }

    public IReadOnlyList<float> Samples => _samples;

    public void Push(float level)
    {
        Array.Copy(_samples, 1, _samples, 0, _samples.Length - 1);
        _samples[^1] = Math.Clamp(level, 0, 1);
    }

    public void Reset() => Array.Clear(_samples);
}
