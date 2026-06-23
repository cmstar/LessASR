using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.Core.Tests.Dictation;

public sealed class ContinuousDictationTextMergeTests
{
    [Fact]
    public void MergeCompletedSegments_JoinsWithNewLine_SkipsNonCompleted()
    {
        var segments = new[]
        {
            new ContinuousDictationSegment(Guid.NewGuid(), ContinuousSegmentState.Completed, "第一句", null),
            new ContinuousDictationSegment(Guid.NewGuid(), ContinuousSegmentState.Transcribing, "", null),
            new ContinuousDictationSegment(Guid.NewGuid(), ContinuousSegmentState.Completed, "第二句", null),
            new ContinuousDictationSegment(Guid.NewGuid(), ContinuousSegmentState.Failed, "", "超时")
        };

        var merged = ContinuousDictationTextMerge.MergeCompletedSegments(segments);

        Assert.Equal("第一句\n第二句", merged);
    }

    [Fact]
    public void MergeCompletedSegments_WhenNoneCompleted_ReturnsEmpty()
    {
        var segments = new[]
        {
            new ContinuousDictationSegment(Guid.NewGuid(), ContinuousSegmentState.WaitingInput, "", null)
        };

        Assert.Equal(string.Empty, ContinuousDictationTextMerge.MergeCompletedSegments(segments));
    }
}
