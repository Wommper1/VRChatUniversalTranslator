using System.Runtime.InteropServices;

namespace VRChatTranslator.Audio;

public static class AudioLevelCalculator
{
    public static (double Peak, double Rms) MeasureIeeeFloat32(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < sizeof(float))
        {
            return (0, 0);
        }

        var alignedLength = buffer.Length - (buffer.Length % sizeof(float));
        var samples = MemoryMarshal.Cast<byte, float>(buffer[..alignedLength]);
        double peak = 0;
        double sumOfSquares = 0;
        var validSamples = 0;

        foreach (var sample in samples)
        {
            if (!float.IsFinite(sample))
            {
                continue;
            }

            var magnitude = Math.Min(Math.Abs((double)sample), 1.0);
            peak = Math.Max(peak, magnitude);
            sumOfSquares += magnitude * magnitude;
            validSamples++;
        }

        return validSamples == 0
            ? (0, 0)
            : (peak, Math.Sqrt(sumOfSquares / validSamples));
    }
}

