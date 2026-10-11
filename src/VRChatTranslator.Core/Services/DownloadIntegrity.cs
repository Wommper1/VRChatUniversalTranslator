using System.Security.Cryptography;

namespace VRChatTranslator.Core.Services;

/// <summary>
/// Checks a downloaded file against the exact size and SHA-256 recorded for it, so a truncated, corrupted or swapped
/// download is rejected before the app ever loads it. Models are data, but they are parsed by native code.
/// </summary>
public static class DownloadIntegrity
{
    public static async Task<string> Sha256HexAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    /// <summary>Throws <see cref="InvalidDataException"/> unless the file is exactly the expected size and has the expected SHA-256.</summary>
    public static async Task VerifyAsync(string path, long expectedBytes, string expectedSha256, string description, CancellationToken cancellationToken = default)
    {
        var length = new FileInfo(path).Length;
        if (length != expectedBytes)
        {
            throw new InvalidDataException($"The downloaded {description} is {length:N0} bytes but {expectedBytes:N0} were expected. It was discarded; please try again.");
        }

        var actual = await Sha256HexAsync(path, cancellationToken);
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The downloaded {description} failed its integrity check (SHA-256 mismatch). It was discarded; please try again.");
        }
    }
}
