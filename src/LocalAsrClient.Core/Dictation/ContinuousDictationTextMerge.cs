namespace LocalAsrClient.Core.Dictation;

public static class ContinuousDictationTextMerge
{
    public static string MergeCompletedSegments(IEnumerable<ContinuousDictationSegment> segments)
    {
        return string.Join(
            "\n",
            segments
                .Where(s => s.State == ContinuousSegmentState.Completed && !string.IsNullOrWhiteSpace(s.Text))
                .Select(s => s.Text));
    }
}
