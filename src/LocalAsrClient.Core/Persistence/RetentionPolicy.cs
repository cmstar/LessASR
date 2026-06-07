namespace LocalAsrClient.Core.Persistence;

public enum TranscriptRetentionPolicy
{
    Disabled = 0,
    OneDay = 1,
    SevenDays = 7,
    OneMonth = 30
}

public static class TranscriptRetentionPolicyExtensions
{
    public static TimeSpan? ToTimeSpan(this TranscriptRetentionPolicy policy)
    {
        return policy switch
        {
            TranscriptRetentionPolicy.Disabled => null,
            TranscriptRetentionPolicy.OneDay => TimeSpan.FromDays(1),
            TranscriptRetentionPolicy.SevenDays => TimeSpan.FromDays(7),
            TranscriptRetentionPolicy.OneMonth => TimeSpan.FromDays(30),
            _ => TimeSpan.FromDays(7)
        };
    }
}