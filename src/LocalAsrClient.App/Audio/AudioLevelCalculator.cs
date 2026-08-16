using System.Buffers.Binary;

namespace LocalAsrClient.App.Audio;

internal static class AudioLevelCalculator
{
    private const float NoiseFloorDb = -50;
    private const float FullScaleDb = -10;
    private static readonly float NoiseFloorRms = MathF.Pow(10, NoiseFloorDb / 20);

    public static float CalculateNormalizedRms(ReadOnlySpan<byte> pcm16)
    {
        var sampleCount = pcm16.Length / sizeof(short);
        if (sampleCount == 0)
        {
            return 0;
        }

        double sumOfSquares = 0;
        for (var index = 0; index < sampleCount; index++)
        {
            var sample = BinaryPrimitives.ReadInt16LittleEndian(
                pcm16.Slice(index * sizeof(short), sizeof(short)));
            var normalizedSample = sample / 32768f;
            sumOfSquares += normalizedSample * normalizedSample;
        }

        var rms = (float)Math.Sqrt(sumOfSquares / sampleCount);
        if (rms <= NoiseFloorRms)
        {
            return 0;
        }

        var decibels = 20 * MathF.Log10(rms);
        return Math.Clamp((decibels - NoiseFloorDb) / (FullScaleDb - NoiseFloorDb), 0, 1);
    }
}
