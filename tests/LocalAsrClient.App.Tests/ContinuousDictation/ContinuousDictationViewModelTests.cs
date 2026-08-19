using System.Collections.Specialized;
using LocalAsrClient.App.ContinuousDictation;
using LocalAsrClient.Core.Dictation;

namespace LocalAsrClient.App.Tests.ContinuousDictation;

public sealed class ContinuousDictationViewModelTests
{
    [Fact]
    public void ApplySnapshot_WhenExistingSegmentIsUnchanged_PreservesCollectionContainer()
    {
        var segment = CompletedSegment("原始文本");
        var viewModel = CreateViewModel();
        viewModel.ApplySnapshot(Snapshot(segment));
        var existingViewModel = Assert.Single(viewModel.Segments);
        var collectionChanges = new List<NotifyCollectionChangedAction>();
        viewModel.Segments.CollectionChanged += (_, args) => collectionChanges.Add(args.Action);

        viewModel.ApplySnapshot(Snapshot(segment));

        Assert.Same(existingViewModel, Assert.Single(viewModel.Segments));
        Assert.Empty(collectionChanges);
    }

    [Fact]
    public void ApplySnapshot_AfterUserEdit_DoesNotReapplySameText()
    {
        var segment = CompletedSegment("原始文本");
        var viewModel = CreateViewModel();
        viewModel.ApplySnapshot(Snapshot(segment));
        var segmentViewModel = Assert.Single(viewModel.Segments);
        segmentViewModel.Text = "原始文本A";
        var changedProperties = new List<string?>();
        segmentViewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName);

        viewModel.ApplySnapshot(Snapshot(segment with { Text = "原始文本A" }));

        Assert.DoesNotContain(nameof(ContinuousSegmentViewModel.Text), changedProperties);
    }

    private static ContinuousDictationViewModel CreateViewModel() => new(
        updateSegmentText: (_, _) => { },
        buildHistoryText: () => string.Empty,
        onClose: () => { },
        onEndRecording: () => { });

    private static ContinuousDictationSegment CompletedSegment(string text) => new(
        Guid.NewGuid(),
        ContinuousSegmentState.Completed,
        text,
        ErrorMessage: null);

    private static ContinuousDictationSnapshot Snapshot(params ContinuousDictationSegment[] segments) => new(
        segments,
        IsRecordingActive: false,
        CompletedCount: segments.Count(segment => segment.State == ContinuousSegmentState.Completed),
        TotalCount: segments.Length,
        BannerMessage: null);
}
