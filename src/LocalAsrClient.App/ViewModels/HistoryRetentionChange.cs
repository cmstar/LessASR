using LocalAsrClient.Core.Persistence;

namespace LocalAsrClient.App.ViewModels;

public sealed record HistoryRetentionChange(
    TranscriptRetentionPolicy PreviousPolicy,
    TranscriptRetentionPolicy NewPolicy,
    int DeleteCount)
{
    public string PreviousPolicyDisplayName => ToDisplayName(PreviousPolicy);

    public string NewPolicyDisplayName => ToDisplayName(NewPolicy);

    public static bool IsShortening(
        TranscriptRetentionPolicy previousPolicy,
        TranscriptRetentionPolicy newPolicy)
    {
        return ToRetention(previousPolicy) > ToRetention(newPolicy);
    }

    private static TimeSpan ToRetention(TranscriptRetentionPolicy policy)
    {
        return policy == TranscriptRetentionPolicy.Disabled
            ? TimeSpan.Zero
            : policy.ToTimeSpan() ?? TimeSpan.Zero;
    }

    private static string ToDisplayName(TranscriptRetentionPolicy policy)
    {
        return policy switch
        {
            TranscriptRetentionPolicy.Disabled => "关闭",
            TranscriptRetentionPolicy.OneDay => "1 天",
            TranscriptRetentionPolicy.SevenDays => "7 天",
            TranscriptRetentionPolicy.OneMonth => "1 个月",
            _ => "未知"
        };
    }
}
