using System.Buffers.Binary;
using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Audio;

public static class FloatWaveFile
{
    private const int HeaderSize = 44;
    private const ushort IeeeFloatFormatTag = 3;

    public static async Task WriteExactDurationAsync(
        string path,
        AudioFormatInfo format,
        ReadOnlyMemory<byte> capturedData,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(format);
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        if (format.BitsPerSample != 32 || !format.Encoding.Contains("float", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("The diagnostic writer currently accepts 32-bit IEEE-float capture data.");
        }

        var blockAlign = checked(format.Channels * format.BitsPerSample / 8);
        var requestedFrames = checked((long)Math.Round(format.SampleRate * duration.TotalSeconds));
        var requestedBytesLong = checked(requestedFrames * blockAlign);
        if (requestedBytesLong > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "The requested diagnostic file is too large.");
        }

        var requestedBytes = (int)requestedBytesLong;
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new InvalidOperationException("The diagnostic path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".partial";

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var header = CreateHeader(format, requestedBytes, blockAlign);
                await stream.WriteAsync(header, cancellationToken);

                var bytesToCopy = Math.Min(capturedData.Length - (capturedData.Length % blockAlign), requestedBytes);
                if (bytesToCopy > 0)
                {
                    await stream.WriteAsync(capturedData[..bytesToCopy], cancellationToken);
                }

                var remaining = requestedBytes - bytesToCopy;
                var silence = new byte[Math.Min(64 * 1024, Math.Max(blockAlign, remaining))];
                while (remaining > 0)
                {
                    var count = Math.Min(remaining, silence.Length);
                    await stream.WriteAsync(silence.AsMemory(0, count), cancellationToken);
                    remaining -= count;
                }

                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    private static byte[] CreateHeader(AudioFormatInfo format, int dataLength, int blockAlign)
    {
        var header = new byte[HeaderSize];
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 36 + dataLength);
        "WAVE"u8.CopyTo(header.AsSpan(8));
        "fmt "u8.CopyTo(header.AsSpan(12));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), 16);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(20), IeeeFloatFormatTag);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(22), checked((ushort)format.Channels));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), format.SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28), checked(format.SampleRate * blockAlign));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(32), checked((ushort)blockAlign));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(34), checked((ushort)format.BitsPerSample));
        "data"u8.CopyTo(header.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(40), dataLength);
        return header;
    }
}
