using LocalAsrClient.App.Overlay;
using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.App.Tests.Overlay;

public sealed class OverlayViewModelTests
{
    [Fact]
    public void RecordingState_UsesCompactInteractiveLayout()
    {
        var submitted = false;
        var viewModel = new OverlayViewModel(onSubmit: () => submitted = true);

        viewModel.ShowState(OverlayState.Recording, "聆听中");
        viewModel.SubmitCommand.Execute(null);

        Assert.True(viewModel.ShowRecordingLayout);
        Assert.False(viewModel.ShowStatusLayout);
        Assert.False(viewModel.ShowCopyLayout);
        Assert.Equal(118, viewModel.OverlayWidth);
        Assert.Equal(32, viewModel.OverlayMinHeight);
        Assert.True(submitted);
    }

    [Theory]
    [InlineData(OverlayState.Transcribing, 96)]
    [InlineData(OverlayState.Injected, 86)]
    [InlineData(OverlayState.LoadingModel, 118)]
    [InlineData(OverlayState.Error, 142)]
    public void CompactStatus_UsesApprovedWidth(OverlayState state, double expectedWidth)
    {
        var viewModel = new OverlayViewModel();

        viewModel.ShowState(state, "状态提示");

        Assert.False(viewModel.ShowRecordingLayout);
        Assert.True(viewModel.ShowStatusLayout);
        Assert.False(viewModel.ShowCopyLayout);
        Assert.Equal(expectedWidth, viewModel.OverlayWidth);
        Assert.Equal(32, viewModel.OverlayMinHeight);
    }

    [Fact]
    public void ResultThatNeedsCopy_UsesExpandedLayout()
    {
        var viewModel = new OverlayViewModel();

        viewModel.ShowState(OverlayState.ResultNeedsAction, "未找到可输入位置", "待复制文本");

        Assert.False(viewModel.ShowRecordingLayout);
        Assert.False(viewModel.ShowStatusLayout);
        Assert.True(viewModel.ShowCopyLayout);
        Assert.Equal(320, viewModel.OverlayWidth);
        Assert.Equal(148, viewModel.OverlayMinHeight);
    }

    [Fact]
    public void InPlaceRecording_BeforeFirstBoundary_KeepsCompactWaveformLayout()
    {
        var viewModel = new OverlayViewModel();

        viewModel.ApplyInPlaceStatus(new InPlaceDictationStatus(
            InPlaceDictationState.Recording,
            [Segment(ContinuousSegmentState.WaitingInput)],
            IsRecordingActive: true,
            HasSegmented: false,
            "聆听中"));

        Assert.True(viewModel.ShowRecordingLayout);
        Assert.False(viewModel.ShowSegmentLayout);
        Assert.Equal(118, viewModel.OverlayWidth);
        Assert.Empty(viewModel.Segments);
    }

    [Fact]
    public void InPlaceRecording_AfterBoundary_GrowsUpwardAndKeepsRecordingRow()
    {
        var viewModel = new OverlayViewModel();

        viewModel.ApplyInPlaceStatus(new InPlaceDictationStatus(
            InPlaceDictationState.Recording,
            [
                Segment(ContinuousSegmentState.Completed, "第一句"),
                Segment(ContinuousSegmentState.Failed, error: "超时"),
                Segment(ContinuousSegmentState.WaitingInput)
            ],
            IsRecordingActive: true,
            HasSegmented: true,
            "聆听中"));

        Assert.True(viewModel.ShowSegmentLayout);
        Assert.True(viewModel.ShowRecordingLayout);
        Assert.Equal(360, viewModel.OverlayWidth);
        Assert.Collection(
            viewModel.Segments,
            first => Assert.Equal("第一句", first.Text),
            failed => Assert.Equal("识别失败：超时", failed.Placeholder));
        Assert.All(viewModel.Segments, segment => Assert.False(segment.IsEditable));
    }

    [Fact]
    public void InPlaceReview_MakesOnlyCompletedSegmentsEditable()
    {
        var edited = new List<(Guid Id, string Text)>();
        var completed = Segment(ContinuousSegmentState.Completed, "原文");
        var viewModel = new OverlayViewModel(onSegmentTextChanged: (id, text) => edited.Add((id, text)));

        viewModel.ApplyInPlaceStatus(new InPlaceDictationStatus(
            InPlaceDictationState.Reviewing,
            [completed, Segment(ContinuousSegmentState.Failed, error: "失败")],
            IsRecordingActive: false,
            HasSegmented: true,
            "检查已识别内容"));
        viewModel.Segments[0].Text = "修订";

        Assert.True(viewModel.ShowReviewLayout);
        Assert.True(viewModel.Segments[0].IsEditable);
        Assert.False(viewModel.Segments[1].IsEditable);
        Assert.Equal((completed.Id, "修订"), Assert.Single(edited));
    }

    private static ContinuousDictationSegment Segment(
        ContinuousSegmentState state,
        string text = "",
        string? error = null) => new(Guid.NewGuid(), state, text, error);
}
