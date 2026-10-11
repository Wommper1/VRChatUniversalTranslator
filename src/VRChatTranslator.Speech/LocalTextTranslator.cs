using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using VRChatTranslator.Core.Services;

namespace VRChatTranslator.Speech;

public sealed class LocalTextTranslator : IAsyncDisposable
{
    // The largest published language pack is about 272 MB. These limits leave headroom without letting a bad download fill the disk.
    private const long MaximumPackDownloadBytes = 512L * 1024 * 1024;
    private const long MaximumPackUnpackedBytes = 1024L * 1024 * 1024;
    private const int MaximumPackEntries = 10_000;

    // Redirects are off: the index and every pack are served directly by their own hosts, and a redirect elsewhere is refused.
    private static readonly HttpClient Http = CreateHttpClient();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _installGate = new(1, 1);
    private Process? _worker;
    public string DataRoot { get; }
    private string TranslationRoot => Path.Combine(DataRoot, "translation");
    public string PythonPath => Path.Combine(TranslationRoot, "python-package", "tools", "python.exe");
    public bool IsRuntimeReady => File.Exists(PythonPath) && File.Exists(Path.Combine(TranslationRoot, "ready-v2.txt"));

    public LocalTextTranslator(string? dataRoot = null) => DataRoot = dataRoot ?? AppPaths.DataRoot;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("VRChatUniversalTranslator");
        return client;
    }

    /// <summary>Windows PowerShell by its full path, so a look-alike file in the app or working folder is never run instead.</summary>
    public static string PowerShellPath => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    public async Task SetupAsync(IProgress<string>? progress, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            StopWorker();
            var script = Path.Combine(AppContext.BaseDirectory, "tools", "setup-translation.ps1");
            var info = new ProcessStartInfo(PowerShellPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-DataRoot", DataRoot }) info.ArgumentList.Add(arg);
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start language-tools setup.");
            using var registration = token.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
            var errors = process.StandardError.ReadToEndAsync(token);
            while (await process.StandardOutput.ReadLineAsync(token) is { } line) progress?.Report(line);
            await process.WaitForExitAsync(token);
            if (process.ExitCode != 0) throw new InvalidOperationException("Language tools setup failed: " + (await errors)[..Math.Min((await errors).Length, 1000)]);
        }
        finally { _gate.Release(); }
    }

    public bool CanTranslate(string source, string target) => source == target || FindPack(source, target) is not null ||
        (source != "en" && target != "en" && FindPack(source, "en") is not null && FindPack("en", target) is not null);

    public IReadOnlyList<string> InstalledPairs
    {
        get
        {
            var root = Path.Combine(TranslationRoot, "packs");
            if (!Directory.Exists(root)) return [];
            return Directory.EnumerateDirectories(root).Select(Path.GetFileName)
                .Select(name => name?.Split('_') ?? []).Where(pair => pair.Length == 2 && FindPack(pair[0], pair[1]) is not null)
                .Select(pair => $"{LanguageCatalog.GetName(pair[0])} → {LanguageCatalog.GetName(pair[1])}")
                .Order(StringComparer.CurrentCulture).ToArray();
        }
    }

    public string InstalledSummary => InstalledPairs is { Count: > 0 } pairs ? string.Join(", ", pairs) : "No additional language packs installed";

    public string OutputPackStatus(string target)
    {
        if (target == "en") return "English output uses the speech model. No additional language pack is needed.";
        var pair = $"English → {LanguageCatalog.GetName(target)}";
        if (!CanTranslate("en", target)) return pair + " · not downloaded. Download once, then translate offline.";
        return pair + (IsRuntimeReady ? " · installed and ready offline." : " · pack installed; local language tools still need setup.");
    }

    private string? FindPack(string source, string target)
    {
        if (!ValidCode(source) || !ValidCode(target)) return null;
        var directory = Path.Combine(TranslationRoot, "packs", source + "_" + target);
        if (!File.Exists(Path.Combine(directory, "installed.txt"))) return null;
        return Directory.EnumerateFiles(directory, "metadata.json", SearchOption.AllDirectories).Select(Path.GetDirectoryName)
            .FirstOrDefault(root => root is not null && File.Exists(Path.Combine(root, "model", "model.bin")) &&
                (File.Exists(Path.Combine(root, "sentencepiece.model")) || File.Exists(Path.Combine(root, "bpe.model"))));
    }

    public async Task InstallPairAsync(string source, string target, IProgress<string>? progress, CancellationToken token = default)
    {
        if (!ValidCode(source) || !ValidCode(target)) throw new ArgumentException("Choose explicit supported languages.");
        if (source == target || CanTranslate(source, target)) return;
        await _installGate.WaitAsync(token);
        try
        {
            if (CanTranslate(source, target)) return;
            using var index = JsonDocument.Parse(await Http.GetStringAsync("https://raw.githubusercontent.com/argosopentech/argospm-index/main/index.json", token));
            var packages = index.RootElement.EnumerateArray().ToArray();
            var direct = packages.FirstOrDefault(p => p.GetProperty("from_code").GetString() == source && p.GetProperty("to_code").GetString() == target);
            if (direct.ValueKind != JsonValueKind.Undefined) await InstallPackAsync(direct, progress, token);
            else
            {
                foreach (var pair in new[] { (source, "en"), ("en", target) })
                {
                    if (pair.Item1 == pair.Item2 || FindPack(pair.Item1, pair.Item2) is not null) continue;
                    var package = packages.FirstOrDefault(p => p.GetProperty("from_code").GetString() == pair.Item1 && p.GetProperty("to_code").GetString() == pair.Item2);
                    if (package.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException($"No local language pack is available for {pair.Item1} → {pair.Item2}.");
                    await InstallPackAsync(package, progress, token);
                }
            }
        }
        finally { _installGate.Release(); }
    }

    private async Task InstallPackAsync(JsonElement package, IProgress<string>? progress, CancellationToken token)
    {
        var source = package.GetProperty("from_code").GetString()!; var target = package.GetProperty("to_code").GetString()!;
        if (!ValidCode(source) || !ValidCode(target)) throw new InvalidDataException("Invalid package language metadata.");
        var link = package.GetProperty("links").EnumerateArray().Select(x => x.GetString()).FirstOrDefault(x => x?.StartsWith("https://argos-net.com/", StringComparison.Ordinal) == true)
            ?? throw new InvalidDataException("Language pack has no trusted HTTPS download.");
        var directory = Path.Combine(TranslationRoot, "packs", source + "_" + target);
        Directory.CreateDirectory(directory);
        var archivePath = Path.Combine(directory, "pack.download");
        progress?.Report($"Downloading {source} → {target} language pack…");
        try
        {
            using var response = await Http.GetAsync(link, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            const long maximumBytes = MaximumPackDownloadBytes;
            if (total > maximumBytes) throw new InvalidDataException("Language pack is too large.");
            await using (var input = await response.Content.ReadAsStreamAsync(token))
            await using (var file = File.Create(archivePath))
            {
                var buffer = new byte[128 * 1024];
                long received = 0;
                var reportTimer = Stopwatch.StartNew();
                int read;
                while ((read = await input.ReadAsync(buffer, token)) != 0)
                {
                    received += read;
                    if (received > maximumBytes) throw new InvalidDataException("Language pack is too large.");
                    await file.WriteAsync(buffer.AsMemory(0, read), token);
                    if (reportTimer.ElapsedMilliseconds >= 250)
                    {
                        progress?.Report(total is > 0
                            ? $"Downloading {LanguageCatalog.GetName(target)}: {received * 100d / total:0}% ({received / 1048576d:0.0} MB)"
                            : $"Downloading {LanguageCatalog.GetName(target)}: {received / 1048576d:0.0} MB");
                        reportTimer.Restart();
                    }
                }
                if (total is not null && received != total) throw new InvalidDataException("Download was interrupted. Please retry.");
            }
            progress?.Report($"Unpacking {LanguageCatalog.GetName(source)} → {LanguageCatalog.GetName(target)}…");
            token.ThrowIfCancellationRequested();
            using (var archive = ZipFile.OpenRead(archivePath))
            {
                if (archive.Entries.Count > MaximumPackEntries) throw new InvalidDataException("Language pack has too many files.");
                if (archive.Entries.Sum(e => e.Length) > MaximumPackUnpackedBytes) throw new InvalidDataException("Language pack is too large.");
                // .NET's extractor rejects entries escaping the destination directory.
                archive.ExtractToDirectory(directory, overwriteFiles: true);
            }
            // The sizes in a ZIP header are only claims; check what was really written.
            if (Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) > MaximumPackUnpackedBytes + maximumBytes)
                throw new InvalidDataException("Language pack unpacked larger than its declared size.");
            var metadataFile = Directory.EnumerateFiles(directory, "metadata.json", SearchOption.AllDirectories).Single();
            using var metadata = JsonDocument.Parse(await File.ReadAllTextAsync(metadataFile, token));
            if (metadata.RootElement.GetProperty("from_code").GetString() != source || metadata.RootElement.GetProperty("to_code").GetString() != target)
                throw new InvalidDataException("Downloaded language pair does not match the request.");
            var modelRoot = Path.GetDirectoryName(metadataFile)!;
            if ((!File.Exists(Path.Combine(modelRoot, "sentencepiece.model")) && !File.Exists(Path.Combine(modelRoot, "bpe.model"))) || !File.Exists(Path.Combine(modelRoot, "model", "model.bin")))
                throw new InvalidDataException("This language pack format is not supported.");
            await File.WriteAllTextAsync(Path.Combine(directory, "installed.txt"), package.GetRawText(), token);
            progress?.Report($"Installed {source} → {target}; works offline.");
        }
        finally { if (File.Exists(archivePath)) File.Delete(archivePath); }
    }

    public async Task<string> TranslateAsync(string text, string source, string target, CancellationToken token = default)
    {
        if (source == target || string.IsNullOrWhiteSpace(text)) return text;
        if (text.Length > 4000) throw new ArgumentException("Translate at most 4000 characters at a time.");
        var packs = GetPacks(source, target);
        using var result = JsonDocument.Parse(await RequestAsync(new { text, packs }, token));
        return result.RootElement.GetProperty("text").GetString() ?? text;
    }

    public async Task PreloadAsync(string source, string target, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (source == target) return;
        var packs = GetPacks(source, target);
        using var result = JsonDocument.Parse(await RequestAsync(new { preload = true, packs }, token));
        if (!result.RootElement.TryGetProperty("ready", out var ready) || !ready.GetBoolean())
            throw new InvalidOperationException("Language pack did not finish loading.");
    }

    private string[] GetPacks(string source, string target)
    {
        if (!IsRuntimeReady) throw new InvalidOperationException("Set up local language tools in Settings → Offline language packs.");
        if (FindPack(source, target) is { } direct) return [direct];
        if (FindPack(source, "en") is { } first && FindPack("en", target) is { } second) return [first, second];
        throw new InvalidOperationException($"Download the {LanguageCatalog.GetName(source)} → {LanguageCatalog.GetName(target)} pack first. Existing text has been preserved.");
    }

    private async Task<string> RequestAsync(object request, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_worker is null || _worker.HasExited)
            {
                StopWorker();
                var info = new ProcessStartInfo(PythonPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8 };
                foreach (var arg in new[] { "-I", "-u", Path.Combine(AppContext.BaseDirectory, "tools", "translate.py") }) info.ArgumentList.Add(arg);
                _worker = Process.Start(info) ?? throw new InvalidOperationException("Local translation worker did not start.");
                _worker.ErrorDataReceived += (_, _) => { }; // Drain without recording conversation-bearing native messages.
                _worker.BeginErrorReadLine();
            }
            await _worker.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), token);
            await _worker.StandardInput.FlushAsync(token);
            var line = await _worker.StandardOutput.ReadLineAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(60), token)
                ?? throw new InvalidOperationException("Local translation worker exited.");
            using var result = JsonDocument.Parse(line);
            if (result.RootElement.TryGetProperty("error", out var error)) throw new InvalidOperationException(error.GetString());
            return line;
        }
        catch { StopWorker(); throw; }
        finally { _gate.Release(); }
    }

    private static bool ValidCode(string code) => code.Length is >= 2 and <= 8 && code.All(c => c is >= 'a' and <= 'z');
    private void StopWorker()
    {
        if (_worker is null) return;
        try { if (!_worker.HasExited) _worker.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        _worker.Dispose(); _worker = null;
    }
    public async ValueTask DisposeAsync() { await _gate.WaitAsync(); try { StopWorker(); } finally { _gate.Release(); } }
}
