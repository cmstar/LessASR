using System.Text;

namespace LocalAsrClient.Core.Dictation;

public static class ContinuousDictationTextMerge
{
    public static string MergeCompletedSegments(IEnumerable<ContinuousDictationSegment> segments)
    {
        var completedTexts = segments
            .Where(s => s.State == ContinuousSegmentState.Completed && !string.IsNullOrWhiteSpace(s.Text))
            .Select(s => s.Text)
            .ToList();

        if (completedTexts.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(completedTexts[0]);
        for (var i = 1; i < completedTexts.Count; i++)
        {
            if (!EndsWithLineBreak(builder))
            {
                builder.Append('\n');
            }

            builder.Append(completedTexts[i]);
        }

        return builder.ToString();
    }

    private static bool EndsWithLineBreak(StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return false;
        }

        if (builder[builder.Length - 1] == '\n')
        {
            return true;
        }

        return builder.Length >= 2
            && builder[builder.Length - 2] == '\r'
            && builder[builder.Length - 1] == '\n';
    }
}
