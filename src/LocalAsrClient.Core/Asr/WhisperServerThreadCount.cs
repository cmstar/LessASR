namespace LocalAsrClient.Core.Asr;

public static class WhisperServerThreadCount
{
    public static int Resolve(int? configuredThreadCount)
    {
        return configuredThreadCount ?? RecommendForCurrentMachine();
    }

    public static int RecommendForCurrentMachine()
    {
        return RecommendForLogicalProcessorCount(Environment.ProcessorCount);
    }

    public static int RecommendForLogicalProcessorCount(int logicalProcessorCount)
    {
        if (logicalProcessorCount >= 17)
        {
            return 12;
        }

        if (logicalProcessorCount >= 16)
        {
            return 10;
        }

        if (logicalProcessorCount >= 12)
        {
            return 8;
        }

        if (logicalProcessorCount >= 8)
        {
            return 6;
        }

        return 4;
    }
}
