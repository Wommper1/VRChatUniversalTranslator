using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;
using Whisper.net.Ggml;

namespace VRChatTranslator.Speech;

public sealed class LocalWhisperModelManager
{
    // Sizes and SHA-256 values are the ones published by the model host (Hugging Face, ggerganov/whisper.cpp, LFS object ids).
    // A download is accepted only if it matches exactly; see DownloadIntegrity.
    private static readonly ModelDefinition Tiny = new("ggml-tiny.bin", GgmlType.Tiny, 77_691_713,
        "be07e048e1e599ad46341c8d2a135645097a538221678b7acdd1b1919c6e1b21");
    private static readonly ModelDefinition Base = new("ggml-base.bin", GgmlType.Base, 147_951_465,
        "60ed5bc3dd14eea856493d334349b405782ddcaf0028d4b5df4088345fba2efe");
    private static readonly ModelDefinition Small = new("ggml-small.bin", GgmlType.Small, 487_601_967,
        "1be3a9b2063867b937e64e2ec7483364a79917e157fa98c5d94b5c1fffea987b");

    // Declared after the three definitions above: static fields are initialised in order.
    private static readonly IReadOnlyDictionary<PerformanceMode, ModelDefinition> Models =
        new Dictionary<PerformanceMode, ModelDefinition>
        {
            [PerformanceMode.UltraLight] = Tiny,
            [PerformanceMode.Balanced] = Base,
            [PerformanceMode.GpuFast] = Base,
            [PerformanceMode.Accurate] = Small,
            [PerformanceMode.Custom] = Base
        };

    public string ModelDirectory { get; } = AppPaths.ModelsDirectory;

    public string BalancedModelPath => GetModelPath(PerformanceMode.Balanced);
    public bool IsBalancedModelInstalled => IsModelInstalled(PerformanceMode.Balanced);

    public string GetModelPath(PerformanceMode mode) =>
        Path.Combine(ModelDirectory, GetDefinition(mode).FileName);

    /// <summary>Installed means present at exactly the published size, so a partial or foreign file is not trusted.</summary>
    public bool IsModelInstalled(PerformanceMode mode)
    {
        var path = GetModelPath(mode);
        return File.Exists(path) && new FileInfo(path).Length == GetDefinition(mode).ExpectedBytes;
    }

    public Task<string> DownloadBalancedModelAsync(
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        DownloadModelAsync(PerformanceMode.Balanced, progress, cancellationToken);

    public async Task<string> DownloadModelAsync(
        PerformanceMode mode,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var definition = GetDefinition(mode);
        var targetPath = GetModelPath(mode);
        Directory.CreateDirectory(ModelDirectory);
        if (IsModelInstalled(mode))
        {
            return targetPath;
        }

        var temporaryPath = targetPath + ".download";
        try
        {
            await using var modelStream = await WhisperGgmlDownloader.Default
                .GetGgmlModelAsync(definition.GgmlType)
                .WaitAsync(cancellationToken);
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                long total = 0;
                while (true)
                {
                    var read = await modelStream.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > definition.ExpectedBytes)
                    {
                        throw new InvalidDataException("The Whisper model download is larger than expected and was stopped.");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    progress?.Report(new ModelDownloadProgress(total, definition.ExpectedBytes));
                }

                await destination.FlushAsync(cancellationToken);
            }

            await DownloadIntegrity.VerifyAsync(temporaryPath, definition.ExpectedBytes, definition.Sha256, "Whisper model", cancellationToken);
            File.Move(temporaryPath, targetPath, overwrite: true);
            return targetPath;
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }

    private static ModelDefinition GetDefinition(PerformanceMode mode) =>
        Models.TryGetValue(mode, out var definition) ? definition : Models[PerformanceMode.Balanced];

    private sealed record ModelDefinition(string FileName, GgmlType GgmlType, long ExpectedBytes, string Sha256);
}
