using System.Buffers.Binary;
using LocalAsrClient.App.Audio;

namespace LocalAsrClient.App.Tests.Audio;

public sealed class AudioLevelCalculatorTests
{
    [Fact]
    public void CalculateNormalizedRms_SilenceReturnsZero()
    {
        var pcm = CreatePcm16(0, 0, 0, 0);

        var level = AudioLevelCalculator.CalculateNormalizedRms(pcm);

        Assert.Equal(0, level);
    }

    [Fact]
    public void CalculateNormalizedRms_LowNoiseIsSuppressed()
    {
        var pcm = CreatePcm16(100, -100, 100, -100);

        var level = AudioLevelCalculator.CalculateNormalizedRms(pcm);

        Assert.Equal(0, level);
    }

    [Fact]
    public void CalculateNormalizedRms_SpeechLevelIsNormalized()
    {
        var pcm = CreatePcm16(3277, -3277, 3277, -3277);

        var level = AudioLevelCalculator.CalculateNormalizedRms(pcm);

        Assert.InRange(level, 0.65f, 0.76f);
    }

    [Fact]
    public void CalculateNormalizedRms_ClippedSignalReturnsOne()
    {
        var pcm = CreatePcm16(short.MaxValue, short.MinValue, short.MaxValue, short.MinValue);

        var level = AudioLevelCalculator.CalculateNormalizedRms(pcm);

        Assert.Equal(1, level);
    }

    private static byte[] CreatePcm16(params short[] samples)
    {
        var buffer = new byte[samples.Length * sizeof(short)];
        for (var index = 0; index < samples.Length; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(
                buffer.AsSpan(index * sizeof(short), sizeof(short)),
                samples[index]);
        }

        return buffer;
    }
}
