using LocalAsrClient.Core.Asr;

namespace LocalAsrClient.Core.Tests.Asr;

public sealed class WhisperServerThreadCountTests
{
    [Fact]
    public void Resolve_UsesConfiguredValueWhenSet()
    {
        Assert.Equal(8, WhisperServerThreadCount.Resolve(8));
    }

    [Fact]
    public void Resolve_UsesRecommendedValueWhenNull()
    {
        Assert.Equal(
            WhisperServerThreadCount.RecommendForCurrentMachine(),
            WhisperServerThreadCount.Resolve(null));
    }

    [Theory]
    [InlineData(1, 4)]
    [InlineData(4, 4)]
    [InlineData(7, 4)]
    [InlineData(8, 6)]
    [InlineData(11, 6)]
    [InlineData(12, 8)]
    [InlineData(15, 8)]
    [InlineData(16, 10)]
    [InlineData(17, 12)]
    [InlineData(32, 12)]
    public void RecommendForLogicalProcessorCount_MapsCoreCountToThreadCount(int logicalProcessorCount, int expectedThreads)
    {
        var threads = WhisperServerThreadCount.RecommendForLogicalProcessorCount(logicalProcessorCount);

        Assert.Equal(expectedThreads, threads);
    }
}
