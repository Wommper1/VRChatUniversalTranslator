using VRChatTranslator.Speech;

namespace VRChatTranslator.Core.Tests;

internal static partial class Program
{
    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    private static async Task<int> RunPackDownloadTest(string directory)
    {
        await using var translator = new LocalTextTranslator(directory);
        if (!translator.CanTranslate("en", "fr"))
        {
            using var cancellation = new CancellationTokenSource();
            var canceled = false;
            try
            {
                await translator.InstallPairAsync("en", "fr", new InlineProgress(status =>
                {
                    if (status.StartsWith("Downloading", StringComparison.Ordinal)) cancellation.Cancel();
                }), cancellation.Token);
            }
            catch (OperationCanceledException) { canceled = true; }
            AssertTrue(canceled && !translator.CanTranslate("en", "fr"), "Canceled download must not claim an installed pack.");
            AssertFalse(File.Exists(Path.Combine(directory, "translation", "packs", "en_fr", "pack.download")), "Canceled download must clean up its archive.");
            Console.WriteLine("PASS canceled pack download leaves no installed marker or archive");
        }
        var download = translator.InstallPairAsync("en", "fr", new InlineProgress(Console.WriteLine));
        var whileDownloading = await translator.TranslateAsync("Hello, how are you?", "en", "es").WaitAsync(TimeSpan.FromSeconds(5));
        AssertTrue(whileDownloading.Contains("Hola", StringComparison.OrdinalIgnoreCase), "An existing pair must translate while another downloads.");
        await download;
        await translator.PreloadAsync("en", "fr");
        var french = await translator.TranslateAsync("Hello, how are you?", "en", "fr");
        Console.WriteLine("Downloaded and preloaded French: " + french);
        AssertTrue(french.Contains("Bonjour", StringComparison.OrdinalIgnoreCase) || french.Contains("Salut", StringComparison.OrdinalIgnoreCase), "Downloaded pack must actually translate to French.");
        AssertFalse(french.Contains('▁'), "Tokenizer boundary markers must not leak into captions.");
        return 0;
    }

    private static async Task LanguagePackStatus()
    {
        var directory = Path.Combine(Path.GetTempPath(), "translator-pack-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var translator = new LocalTextTranslator(directory);
            AssertEqual(0, translator.InstalledPairs.Count, "New installations should list no additional packs.");
            AssertTrue(translator.OutputPackStatus("en").Contains("No additional", StringComparison.Ordinal), "English must not ask for a redundant pack.");
            AssertTrue(translator.OutputPackStatus("es").Contains("not downloaded", StringComparison.Ordinal), "Missing packs must be explained.");
            await translator.PreloadAsync("en", "en");
            AssertFalse(Directory.Exists(directory), "Same-language preload must not download or create files.");

            var pack = Path.Combine(directory, "translation", "packs", "en_es");
            Directory.CreateDirectory(pack);
            await File.WriteAllTextAsync(Path.Combine(pack, "metadata.json"), "{}");
            await File.WriteAllTextAsync(Path.Combine(pack, "installed.txt"), "{}");
            AssertFalse(translator.CanTranslate("en", "es"), "A completion marker without a model must not count as installed.");
            Directory.CreateDirectory(Path.Combine(pack, "model"));
            await File.WriteAllBytesAsync(Path.Combine(pack, "model", "model.bin"), [1]);
            await File.WriteAllBytesAsync(Path.Combine(pack, "sentencepiece.model"), [1]);
            AssertTrue(translator.InstalledPairs.Contains("English → Spanish"), "Valid file layout should be listed with readable names.");
            AssertTrue(translator.OutputPackStatus("es").Contains("tools still need setup", StringComparison.Ordinal), "Pack and runtime readiness must be distinct.");
            var runtime = Path.Combine(directory, "translation", "python-package", "tools");
            Directory.CreateDirectory(runtime);
            await File.WriteAllBytesAsync(Path.Combine(runtime, "python.exe"), [1]);
            await File.WriteAllTextAsync(Path.Combine(directory, "translation", "ready-v2.txt"), "test-only-layout");
            AssertTrue(translator.OutputPackStatus("es").Contains("ready offline", StringComparison.Ordinal), "Complete layout should be ready offline.");
            AssertFalse(translator.CanTranslate("../outside", "es"), "Language codes must never escape the pack directory.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
