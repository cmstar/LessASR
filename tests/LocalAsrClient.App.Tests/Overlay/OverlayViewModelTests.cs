using LocalAsrClient.App.Overlay;

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
}
