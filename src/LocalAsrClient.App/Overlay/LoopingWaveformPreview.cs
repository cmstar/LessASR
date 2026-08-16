namespace LocalAsrClient.App.Overlay;

internal sealed class LoopingWaveformPreview
{
    private static readonly float[] Levels =
    [
        0f, 0f, 0.04f, 0.14f, 0.38f,
        0.72f, 0.94f, 0.68f, 0.34f, 0.12f,
        0.06f, 0.24f, 0.56f, 0.82f, 0.60f,
        0.32f, 0.14f, 0.04f, 0f, 0f
    ];

    private int _nextIndex;

    public static int SamplesPerCycle => Levels.Length;

    public static TimeSpan FrameInterval { get; } = TimeSpan.FromMilliseconds(50);

    public float NextLevel()
    {
        var level = Levels[_nextIndex];
        _nextIndex = (_nextIndex + 1) % Levels.Length;
        return level;
    }

    public void Reset() => _nextIndex = 0;
}
