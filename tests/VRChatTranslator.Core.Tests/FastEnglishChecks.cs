using System.Security.Cryptography;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Tar;
using VRChatTranslator.Core.Models;
using VRChatTranslator.Core.Services;
using VRChatTranslator.Core.Settings;
using VRChatTranslator.Speech;

namespace VRChatTranslator.Core.Tests;

internal static partial class Program
{
    private const string FakeArchiveFolder = "fake-model-folder";

    private static ExpectedModelFile Expect(string name, byte[] content) =>
        new(name, content.Length, Convert.ToHexStringLower(SHA256.HashData(content)));

    /// <summary>Builds an in-memory .tar.bz2 with the given (path, content) entries, to exercise the extraction rules.</summary>
    private static MemoryStream BuildTarBz2(params (string Path, byte[] Content)[] entries)
    {
        using var tar = new MemoryStream();
        using (var writer = new TarOutputStream(tar, System.Text.Encoding.UTF8) { IsStreamOwner = false })
        {
            foreach (var (path, content) in entries)
            {
                var entry = TarEntry.CreateTarEntry(path);
                entry.Size = content.Length;
                writer.PutNextEntry(entry);
                writer.Write(content, 0, content.Length);
                writer.CloseEntry();
            }
        }

        tar.Position = 0;
        var compressed = new MemoryStream();
        BZip2.Compress(tar, compressed, isStreamOwner: false, level: 9);
        compressed.Position = 0;
        return compressed;
    }

    private static string NewTempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "vrt-fast-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static async Task TheFastEnglishModelUnpacksOnlyTheTwoExpectedFiles()
    {
        var model = "model bytes"u8.ToArray();
        var tokens = "tokens bytes"u8.ToArray();
        var expected = new[] { Expect("model.int8.onnx", model), Expect("tokens.txt", tokens) };
        var folder = NewTempFolder();
        try
        {
            using var archive = BuildTarBz2(
                ($"./{FakeArchiveFolder}/model.int8.onnx", model),
                ($"./{FakeArchiveFolder}/tokens.txt", tokens),
                ($"./{FakeArchiveFolder}/test_wavs/0.wav", "audio"u8.ToArray()),
                ($"./{FakeArchiveFolder}/README.md", "readme"u8.ToArray()));
            await FastEnglishModel.ExtractAsync(archive, folder, expected, FakeArchiveFolder);
            AssertSequence(new[] { "model.int8.onnx", "tokens.txt" }, Directory.GetFiles(folder).Select(Path.GetFileName).Order().Select(n => n!).ToArray(),
                "Only the two expected files are written; test clips and other entries are left out, and no temporary file remains.");
            AssertTrue(File.ReadAllBytes(Path.Combine(folder, "model.int8.onnx")).SequenceEqual(model), "The model file has the archive's bytes.");
            AssertTrue(FastEnglishModel.IsInstalled(folder) == false, "A fake model of the wrong size is not counted as installed.");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static async Task TheFastEnglishModelRejectsUnsafeWrongOrIncompleteArchives()
    {
        var model = "model bytes"u8.ToArray();
        var tokens = "tokens bytes"u8.ToArray();
        var expected = new[] { Expect("model.int8.onnx", model), Expect("tokens.txt", tokens) };

        async Task AssertRejectedAsync(string why, params (string Path, byte[] Content)[] entries)
        {
            var folder = NewTempFolder();
            try
            {
                using var archive = BuildTarBz2(entries);
                try
                {
                    await FastEnglishModel.ExtractAsync(archive, folder, expected, FakeArchiveFolder);
                    throw new InvalidOperationException($"An archive was accepted but should be rejected: {why}.");
                }
                catch (InvalidDataException)
                {
                    // expected
                }

                AssertEqual(0, Directory.GetFileSystemEntries(folder).Length, $"Nothing may be left on disk after a rejected archive ({why}).");
            }
            finally { Directory.Delete(folder, recursive: true); }
        }

        await AssertRejectedAsync("an entry climbs out of the folder",
            ($"{FakeArchiveFolder}/model.int8.onnx", model), ($"{FakeArchiveFolder}/../../evil.dll", "x"u8.ToArray()), ($"{FakeArchiveFolder}/tokens.txt", tokens));
        await AssertRejectedAsync("an absolute path", ("/etc/passwd", "x"u8.ToArray()));
        await AssertRejectedAsync("a drive path", ("C:/Windows/evil.dll", "x"u8.ToArray()));
        await AssertRejectedAsync("the model file has the wrong size", ($"{FakeArchiveFolder}/model.int8.onnx", "much longer model bytes"u8.ToArray()), ($"{FakeArchiveFolder}/tokens.txt", tokens));
        await AssertRejectedAsync("the model file has the right size but the wrong content", ($"{FakeArchiveFolder}/model.int8.onnx", "MODEL BYTES"u8.ToArray()), ($"{FakeArchiveFolder}/tokens.txt", tokens));
        await AssertRejectedAsync("the token list is missing", ($"{FakeArchiveFolder}/model.int8.onnx", model));
        await AssertRejectedAsync("the files are in the wrong folder", ("other-folder/model.int8.onnx", model), ("other-folder/tokens.txt", tokens));
    }

    private static Task TheFastEnglishModelDownloadIsPinnedAndOptional()
    {
        AssertTrue(FastEnglishModel.ArchiveUrl.StartsWith("https://github.com/k2-fsa/sherpa-onnx/releases/download/", StringComparison.Ordinal), "The download comes only from the sherpa-onnx releases over HTTPS.");
        AssertEqual(64, FastEnglishModel.ArchiveSha256.Length, "The archive SHA-256 is pinned.");
        AssertEqual(64, FastEnglishModel.Model.Sha256.Length, "The model SHA-256 is pinned.");
        AssertEqual(64, FastEnglishModel.Tokens.Sha256.Length, "The token list SHA-256 is pinned.");
        var archiveBytes = FastEnglishModel.DownloadBytes;
        AssertTrue(archiveBytes > 50_000_000 && archiveBytes < 200_000_000, "The pinned size is plausible for a 110M int8 model.");
        AssertEqual(false, new AppSettings().FastEnglishEngine, "The fast English engine is off by default.");
        AssertTrue(FastEnglishModel.InstallFolder.StartsWith(AppPaths.ModelsDirectory, StringComparison.OrdinalIgnoreCase), "The model lives with the other models in the app's own data folder.");
        return Task.CompletedTask;
    }

    private static Task EnglishRoutingIsCautiousAboutOtherLanguages()
    {
        AssertTrue(EnglishRouting.UseFastEngine("en", 0), "A user who locked English always gets the fast engine; no check is needed.");
        AssertFalse(EnglishRouting.NeedsLanguageCheck("en"), "A locked language needs no language check.");
        AssertTrue(EnglishRouting.NeedsLanguageCheck("auto"), "Auto-detect needs a language check first.");
        AssertTrue(EnglishRouting.UseFastEngine("auto", 0.97), "Confident English goes to the fast engine.");
        AssertFalse(EnglishRouting.UseFastEngine("auto", 0.60), "Unsure goes to Whisper.");
        AssertFalse(EnglishRouting.UseFastEngine("auto", 0), "Another language goes to Whisper.");
        AssertFalse(EnglishRouting.UseFastEngine("ja", 0.99), "A locked non-English language never uses the English-only engine, whatever a check says.");
        AssertFalse(EnglishRouting.UseFastEngine("es", 1), "Spanish never uses the English-only engine.");
        return Task.CompletedTask;
    }

    private sealed class CannedRecognizer(string text) : ISpeechRecognizer
    {
        public int Calls { get; private set; }
        public string Name => "Canned";
        public Task<RecognitionResult> RecognizeAsync(ReadOnlyMemory<float> samples, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new RecognitionResult(text, "en", 1));
        }

        public void Dispose() { }
    }

    private static async Task TheFastEnglishEngineCaptionsSpeechWithoutLoadingWhisper()
    {
        var folder = NewTempFolder();
        try
        {
            var placeholder = Path.Combine(folder, "unused-whisper-model.bin");
            await File.WriteAllBytesAsync(placeholder, [0]);
            using var recognizer = new CannedRecognizer("hello from the fast engine");
            var captions = new List<CaptionEvent>();
            await using var capture = new CrowdEval.StereoFixtureCapture();
            await using var speech = new LocalWhisperSpeechService { FastEnglishRecognizer = recognizer, FastEnglishOnly = true };
            speech.CaptionProduced += (_, e) => { lock (captions) captions.Add(e.Caption); };
            await speech.StartAsync(capture, placeholder, threads: 2, showOriginalText: false, translateToEnglish: true, sourceLanguage: "auto");
            AssertEqual(SpeechPipelineState.Listening, speech.Current.State, "The service starts listening without any Whisper model.");

            // 1.5 s of loud tone-like noise (speech to the energy gate) then 1 s of silence, fed 20 times faster than real time.
            var loud = new float[48_000 * 2 * 3 / 2];
            for (var i = 0; i < loud.Length; i++) loud[i] = 0.3f * MathF.Sin(i * 0.05f);
            await capture.FeedAsync(loud, speed: 20);
            await capture.FeedAsync(new float[48_000 * 2], speed: 20);
            await WaitUntilAsync(() => { lock (captions) return captions.Count == 1; }, "The fast engine never produced a caption.");
            lock (captions)
            {
                AssertEqual("hello from the fast engine", captions[0].TranslatedText, "The caption is the fast engine's text.");
                AssertEqual("en", captions[0].DetectedLanguage, "The fast engine is English.");
            }

            AssertEqual(1, recognizer.Calls, "One segment was recognised once.");
            AssertEqual(1L, speech.Current.FastEnglishSegments, "The snapshot counts the segment as handled by the fast engine.");
            AssertEqual(0L, speech.Current.WhisperSegments, "Whisper handled nothing.");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}

internal static partial class Program
{
    /// <summary>
    /// Development check: unpacks a real downloaded Parakeet archive with the same code and pinned values the app uses, into a
    /// temporary folder, then verifies the result. Usage: --check-fast-english-archive &lt;path to the .tar.bz2&gt;.
    /// </summary>
    private static async Task<int> CheckFastEnglishArchiveAsync(string archivePath)
    {
        var folder = NewTempFolder();
        try
        {
            await DownloadIntegrity.VerifyAsync(archivePath, FastEnglishModel.ArchiveBytes, FastEnglishModel.ArchiveSha256, "fast English model archive");
            Console.WriteLine("Archive size and SHA-256 match the pinned values.");
            await using (var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await FastEnglishModel.ExtractAsync(stream, folder, [FastEnglishModel.Model, FastEnglishModel.Tokens], FastEnglishModel.ArchiveFolder);
            }

            Console.WriteLine("Installed: " + FastEnglishModel.IsInstalled(folder));
            await FastEnglishModel.VerifyInstalledAsync(folder);
            Console.WriteLine("Both extracted files match their pinned size and SHA-256.");
            Console.WriteLine("Files written: " + string.Join(", ", Directory.GetFiles(folder).Select(Path.GetFileName)));
            using var recognizer = FastEnglishModel.TryCreateRecognizer(2, folder) ?? throw new InvalidOperationException("The recogniser did not start.");
            Console.WriteLine("The recogniser starts from the extracted files: " + recognizer.Name);
            return FastEnglishModel.IsInstalled(folder) && Directory.GetFiles(folder).Length == 2 ? 0 : 1;
        }
        finally { Directory.Delete(folder, recursive: true); }
    }
}

internal static partial class Program
{
    /// <summary>
    /// Development check of the real download path: downloads the model with the app's own installer into an empty folder, verifies
    /// it and starts the recogniser. Usage: --check-fast-english-download &lt;empty folder&gt;. Needs internet; about 104 MB.
    /// </summary>
    private static async Task<int> CheckFastEnglishDownloadAsync(string folder)
    {
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) throw new InvalidOperationException("The folder must be empty or not exist.");
        var lastPercent = -10.0;
        var progress = new Progress<ModelDownloadProgress>(value =>
        {
            if (value.Percentage is { } percent && percent - lastPercent >= 10) { lastPercent = percent; Console.WriteLine($"  downloaded {percent:0}%"); }
        });
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await FastEnglishModel.DownloadAsync(progress, folder);
        Console.WriteLine($"Downloaded and unpacked in {clock.Elapsed.TotalSeconds:0.0} s.");
        Console.WriteLine("Installed: " + FastEnglishModel.IsInstalled(folder));
        await FastEnglishModel.VerifyInstalledAsync(folder);
        Console.WriteLine("Both files match their pinned size and SHA-256.");
        var names = Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).Order().ToArray();
        Console.WriteLine("Folder contents: " + string.Join(", ", names));
        using var recognizer = FastEnglishModel.TryCreateRecognizer(2, folder) ?? throw new InvalidOperationException("The recogniser did not start.");
        Console.WriteLine("The recogniser starts: " + recognizer.Name);
        // Second call must do nothing, quickly.
        var again = System.Diagnostics.Stopwatch.StartNew();
        await FastEnglishModel.DownloadAsync(null, folder);
        Console.WriteLine($"A second download request returned in {again.ElapsedMilliseconds} ms without downloading.");
        return names.Length == 2 && FastEnglishModel.IsInstalled(folder) ? 0 : 1;
    }
}
