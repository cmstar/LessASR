using LocalAsrClient.App.Overlay;

namespace LocalAsrClient.App.Tests.Overlay;

public sealed class WaveformHistoryTests
{
    [Fact]
    public void NewHistory_HasEnoughSilentBarsForExpandedRecordingRow()
    {
        var history = new WaveformHistory();

        Assert.Equal(96, history.Samples.Count);
        Assert.All(history.Samples, sample => Assert.Equal(0, sample));
    }

    [Fact]
    public void Push_ShiftsOlderLevelsFromRightToLeft()
    {
        var history = new WaveformHistory();

        history.Push(0.25f);
        history.Push(0.75f);

        Assert.Equal(0.25f, history.Samples[^2]);
        Assert.Equal(0.75f, history.Samples[^1]);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(2, 1)]
    public void Push_ClampsLevel(float input, float expected)
    {
        var history = new WaveformHistory();

        history.Push(input);

        Assert.Equal(expected, history.Samples[^1]);
    }

    [Fact]
    public void Reset_ReturnsEveryBarToSilence()
    {
        var history = new WaveformHistory();
        history.Push(0.8f);

        history.Reset();

        Assert.All(history.Samples, sample => Assert.Equal(0, sample));
    }
}
