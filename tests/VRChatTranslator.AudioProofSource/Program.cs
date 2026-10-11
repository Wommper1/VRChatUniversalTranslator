using System.Runtime.InteropServices;

namespace VRChatTranslator.AudioProofSource;

internal static partial class Program
{
    private const uint SoundFilename = 0x00020000;
    private const uint SoundSync = 0x0000;

    private static int Main(string[] args)
    {
        if (args is ["--silent", var silentDuration] && int.TryParse(silentDuration, out var silentMilliseconds))
        {
            Thread.Sleep(Math.Clamp(silentMilliseconds, 100, 10_000));
            return 0;
        }

        if (args is ["--tone", var delay, var duration, var frequency] &&
            int.TryParse(delay, out var delayMilliseconds) &&
            int.TryParse(duration, out var durationMilliseconds) &&
            double.TryParse(frequency, out var frequencyHz))
        {
            Thread.Sleep(Math.Clamp(delayMilliseconds, 0, 5_000));
            return PlayGeneratedTone(
                Math.Clamp(durationMilliseconds, 100, 5_000),
                Math.Clamp(frequencyHz, 100, 4_000));
        }

        Console.Error.WriteLine("Usage: --silent <milliseconds> | --tone <delay-ms> <duration-ms> <frequency-hz>");
        return 2;
    }

    private static int PlayGeneratedTone(int durationMilliseconds, double frequencyHz)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"vrchat-translator-proof-{Guid.NewGuid():N}.wav");
        try
        {
            File.WriteAllBytes(filePath, CreateWave(durationMilliseconds, frequencyHz));
            return PlaySound(filePath, 0, SoundFilename | SoundSync) ? 0 : 1;
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static byte[] CreateWave(int durationMilliseconds, double frequencyHz)
    {
        const int sampleRate = 48_000;
        const short channels = 1;
        const short bitsPerSample = 16;
        var sampleCount = sampleRate * durationMilliseconds / 1_000;
        var dataLength = sampleCount * sizeof(short);

        using var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bitsPerSample / 8);
        writer.Write((short)(channels * bitsPerSample / 8));
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataLength);

        for (var index = 0; index < sampleCount; index++)
        {
            var envelope = Math.Min(1.0, Math.Min(index / 480.0, (sampleCount - index) / 480.0));
            var sample = Math.Sin(2 * Math.PI * frequencyHz * index / sampleRate) * 0.15 * envelope;
            writer.Write((short)(sample * short.MaxValue));
        }

        return stream.ToArray();
    }

    [LibraryImport("winmm.dll", EntryPoint = "PlaySoundW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PlaySound(string sound, nint module, uint flags);
}

