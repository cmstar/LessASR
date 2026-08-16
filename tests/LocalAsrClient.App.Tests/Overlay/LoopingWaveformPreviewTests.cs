using LocalAsrClient.App.Overlay;

namespace LocalAsrClient.App.Tests.Overlay;

public sealed class LoopingWaveformPreviewTests
{
    [Fact]
    public void PreviewCycle_ContainsOneSecondAtTwentyFramesPerSecond()
    {
        Assert.Equal(20, LoopingWaveformPreview.SamplesPerCycle);
        Assert.Equal(TimeSpan.FromMilliseconds(50), LoopingWaveformPreview.FrameInterval);
    }

    [Fact]
    public void NextLevel_LoopsAfterOneCompleteCycle()
    {
        var preview = new LoopingWaveformPreview();
        var first = preview.NextLevel();

        for (var index = 1; index < LoopingWaveformPreview.SamplesPerCycle; index++)
        {
            _ = preview.NextLevel();
        }

        Assert.Equal(first, preview.NextLevel());
    }

    [Fact]
    public void PreviewCycle_ContainsSilenceAndSpeechPeak()
    {
        var preview = new LoopingWaveformPreview();
        var levels = Enumerable.Range(0, LoopingWaveformPreview.SamplesPerCycle)
            .Select(_ => preview.NextLevel())
            .ToArray();

        Assert.Contains(0, levels);
        Assert.Contains(levels, level => level >= 0.8f);
    }
}
