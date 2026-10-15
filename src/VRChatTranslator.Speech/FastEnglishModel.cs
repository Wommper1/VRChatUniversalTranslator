using System.Security.Cryptography;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Tar;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;

namespace VRChatTranslator.Speech;

/// <summary>One file the fast English engine needs, with the exact size and SHA-256 it must have.</summary>
public sealed record ExpectedModelFile(string Name, long Bytes, string Sha256);

/// <summary>
/// The optional fast English recogniser: NVIDIA Parakeet TDT-CTC 110M, int8, converted for sherpa-onnx (licence CC-BY-4.0).
/// It is downloaded once, on request, as the upstream archive. The archive is checked against its exact size and SHA-256, then
/// only the two files the app needs are unpacked, each checked again. Nothing else in the archive is written to disk.
/// </summary>
public static class FastEnglishModel
{
    public const string DisplayName = "Parakeet 110M (English, int8)";

    // Recorded from the sherpa-onnx "asr-models" release over HTTPS; GitHub also publishes the archive digest.
    public const string ArchiveUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/sherpa-onnx-nemo-parakeet_tdt_ctc_110m-en-36000-int8.tar.bz2";
    public const long ArchiveBytes = 104_337_827;
    public const string ArchiveSha256 = "17f945007b52ccd8b7200ffc7c5652e9e8e961dfdf479cefcabd06cf5703630b";

    /// <summary>The single folder inside the archive that holds the files.</summary>
    public const string ArchiveFolder = "sherpa-onnx-nemo-parakeet_tdt_ctc_110m-en-36000-int8";

    public static readonly ExpectedModelFile Model = new("model.int8.onnx", 131_652_171, "9177a9146cf32ee0cc8152276ef95116f312018d316be37ccf57f7efea81fc1a");
    public static readonly ExpectedModelFile Tokens = new("tokens.txt", 9_953, "450e56bd2f036fe5b6aa821865838cc5aa9d8b0106134ce9a9ba0664abe6cd10");

    private static readonly ExpectedModelFile[] Files = [Model, Tokens];

    /// <summary>Roughly how much the download is, for the Settings text.</summary>
    public static long DownloadBytes => ArchiveBytes;

    public static string InstallFolder => AppPaths.FastEnglishDirectory;

    public static string ModelPath(string? folder = null) => Path.Combine(folder ?? InstallFolder, Model.Name);

    public static string TokensPath(string? folder = null) => Path.Combine(folder ?? InstallFolder, Tokens.Name);

    /// <summary>Installed means both files are present at exactly the published size. Download and <see cref="VerifyInstalledAsync"/> check the SHA-256.</summary>
    public static bool IsInstalled(string? folder = null) => Files.All(file =>
        File.Exists(Path.Combine(folder ?? InstallFolder, file.Name)) && new FileInfo(Path.Combine(folder ?? InstallFolder, file.Name)).Length == file.Bytes);

    public static async Task VerifyInstalledAsync(string? folder = null, CancellationToken cancellationToken = default)
    {
        foreach (var file in Files)
        {
            await DownloadIntegrity.VerifyAsync(Path.Combine(folder ?? InstallFolder, file.Name), file.Bytes, file.Sha256, "fast English model file", cancellationToken);
        }
    }

    /// <summary>Downloads and installs the model into <paramref name="folder"/> (default: the app's folder). Does nothing if it is already installed.</summary>
    public static async Task DownloadAsync(IProgress<ModelDownloadProgress>? progress = null, string? folder = null, CancellationToken cancellationToken = default)
    {
        var target = folder ?? InstallFolder;
        if (IsInstalled(target)) return;
        System.IO.Directory.CreateDirectory(target);
        var archive = Path.Combine(target, "archive." + Guid.NewGuid().ToString("N") + ".download");
        try
        {
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) })
            using (var response = await http.GetAsync(ArchiveUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is { } announced && announced > ArchiveBytes)
                {
                    throw new InvalidDataException("The fast English model download is larger than expected.");
                }

                await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var file = new FileStream(archive, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[128 * 1024];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) != 0)
                {
                    received += read;
                    if (received > ArchiveBytes) throw new InvalidDataException("The fast English model download is larger than expected and was stopped.");
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progress?.Report(new ModelDownloadProgress(received, ArchiveBytes));
                }
            }

            await DownloadIntegrity.VerifyAsync(archive, ArchiveBytes, ArchiveSha256, "fast English model archive", cancellationToken);
            await using var archiveStream = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await ExtractAsync(archiveStream, target, Files, ArchiveFolder, cancellationToken);
        }
        finally
        {
            if (File.Exists(archive)) File.Delete(archive);
        }
    }

    /// <summary>
    /// Unpacks only <paramref name="expected"/> from a .tar.bz2 stream into <paramref name="destination"/>. An entry that climbs out of its
    /// folder, is too large, or has the wrong checksum rejects the whole archive, and nothing is left behind except the files that passed.
    /// </summary>
    public static async Task ExtractAsync(Stream tarBz2, string destination, IReadOnlyList<ExpectedModelFile> expected, string archiveFolder, CancellationToken cancellationToken = default)
    {
        System.IO.Directory.CreateDirectory(destination);
        var staged = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var bzip = new BZip2InputStream(tarBz2) { IsStreamOwner = false };
            using var tar = new TarInputStream(bzip, System.Text.Encoding.UTF8) { IsStreamOwner = false };
            TarEntry? entry;
            while ((entry = tar.GetNextEntry()) is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = entry.Name.Replace('\\', '/');
                var segments = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (name.StartsWith('/') || segments.Any(s => s == ".." || s.Contains(':')))
                {
                    throw new InvalidDataException("The model archive contains an unsafe path and was rejected.");
                }

                if (entry.IsDirectory) continue;
                // Only "<archive folder>/<expected file>" is wanted; "./" prefixes are ignored. Anything else is skipped, never written.
                var relative = segments.Where(s => s != ".").ToArray();
                if (relative.Length != 2 || !string.Equals(relative[0], archiveFolder, StringComparison.Ordinal)) continue;
                var match = expected.FirstOrDefault(file => string.Equals(file.Name, relative[1], StringComparison.Ordinal));
                if (match is null) continue;
                if (entry.Size != match.Bytes)
                {
                    throw new InvalidDataException($"{match.Name} in the model archive is {entry.Size:N0} bytes but {match.Bytes:N0} were expected.");
                }

                var temporary = Path.Combine(destination, match.Name + "." + Guid.NewGuid().ToString("N") + ".extract");
                staged[match.Name] = temporary;
                await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous))
                {
                    var buffer = new byte[128 * 1024];
                    long written = 0;
                    int read;
                    while ((read = tar.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        written += read;
                        if (written > match.Bytes) throw new InvalidDataException($"{match.Name} in the model archive is larger than expected.");
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                }

                await DownloadIntegrity.VerifyAsync(temporary, match.Bytes, match.Sha256, $"fast English model file {match.Name}", cancellationToken);
            }

            var missing = expected.Where(file => !staged.ContainsKey(file.Name)).Select(file => file.Name).ToArray();
            if (missing.Length > 0) throw new InvalidDataException("The model archive is missing " + string.Join(", ", missing) + ".");
            foreach (var file in expected)
            {
                File.Move(staged[file.Name], Path.Combine(destination, file.Name), overwrite: true);
            }

            staged.Clear();
        }
        finally
        {
            foreach (var leftover in staged.Values)
            {
                if (File.Exists(leftover)) File.Delete(leftover);
            }
        }
    }

    /// <summary>Creates the recogniser, or null when the files are not installed. The caller owns and disposes it.</summary>
    public static SherpaNemoCtcRecognizer? TryCreateRecognizer(int threads, string? folder = null) =>
        IsInstalled(folder) ? new SherpaNemoCtcRecognizer(ModelPath(folder), TokensPath(folder), threads) : null;
}
