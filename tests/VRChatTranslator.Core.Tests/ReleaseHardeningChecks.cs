using System.Security.Cryptography;
using System.Text;
using VRChatTranslator.Core.Services;
using VRChatTranslator.Speech;

namespace VRChatTranslator.Core.Tests;

internal static partial class Program
{
    private static Task AppDataLivesInOneCompanyFolder()
    {
        var root = AppPaths.GetDataRoot(@"C:\Users\Someone\AppData\Local");
        AssertEqual(@"C:\Users\Someone\AppData\Local\TheLevelEverythingCompany\VRChatUniversalTranslator", root, "The data root is one company folder and one app folder.");
        AssertEqual(Path.Combine(AppPaths.DataRoot, "config.json"), AppPaths.ConfigPath, "Settings live directly under the data root.");
        AssertEqual(Path.Combine(AppPaths.DataRoot, "models"), AppPaths.ModelsDirectory, "Models live under the data root.");
        AssertEqual(Path.Combine(AppPaths.DataRoot, "logs"), AppPaths.LogsDirectory, "Logs live under the data root.");
        AssertEqual(Path.Combine(AppPaths.ModelsDirectory, "speaker-eres2net.onnx"), AppPaths.SpeakerModelPath, "The optional speaker model sits beside the speech models.");
        AssertEqual(AppPaths.ConfigPath, new JsonSettingsStore().SettingsPath, "The default settings store must use the shared path.");
        AssertEqual(AppPaths.ModelsDirectory, new LocalWhisperModelManager().ModelDirectory, "The model manager must use the shared path.");
        AssertEqual(AppPaths.DataRoot, new LocalTextTranslator().DataRoot, "The language-pack translator must use the shared path.");
        return Task.CompletedTask;
    }

    private static async Task DownloadsAreAcceptedOnlyWhenTheyMatchExactly()
    {
        var folder = Path.Combine(Path.GetTempPath(), "vrt-integrity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "model.bin");
            await File.WriteAllBytesAsync(file, Encoding.ASCII.GetBytes("abc"));
            const string abc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

            AssertEqual(abc, await DownloadIntegrity.Sha256HexAsync(file), "SHA-256 of a known input.");
            await DownloadIntegrity.VerifyAsync(file, 3, abc, "test file");
            await DownloadIntegrity.VerifyAsync(file, 3, abc.ToUpperInvariant(), "test file");

            await RequireInvalidDataAsync(() => DownloadIntegrity.VerifyAsync(file, 4, abc, "test file"), "A file of the wrong size is refused.");
            var otherHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes("abd")));
            await RequireInvalidDataAsync(() => DownloadIntegrity.VerifyAsync(file, 3, otherHash, "test file"), "Same size but a different hash is refused.");
            await RequireInvalidDataAsync(() => DownloadIntegrity.VerifyAsync(file, 3, "", "test file"), "An empty expected hash never matches.");

            await File.WriteAllBytesAsync(file, Encoding.ASCII.GetBytes("abd"));
            await RequireInvalidDataAsync(() => DownloadIntegrity.VerifyAsync(file, 3, abc, "test file"), "A swapped file of the same size is refused.");
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static async Task RequireInvalidDataAsync(Func<Task> action, string message)
    {
        try { await action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Expected InvalidDataException: " + message);
    }

    private static Task OldDiagnosticLogsAreRemovedAndOthersKept()
    {
        var folder = Path.Combine(Path.GetTempPath(), "vrt-logs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var now = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
            string Make(string name, int daysOld)
            {
                var path = Path.Combine(folder, name);
                File.WriteAllText(path, "line");
                File.SetLastWriteTimeUtc(path, now.AddDays(-daysOld));
                return path;
            }

            var old = Make("translator-20260901.log", 40);
            var recent = Make("translator-20261010.log", 1);
            var edge = Make("translator-20260928.log", 13);
            var unrelated = Make("notes.txt", 400);

            AssertEqual(1, LogRetention.PruneOldLogs(folder, TimeSpan.FromDays(14), now), "Exactly the one old log is removed.");
            AssertFalse(File.Exists(old), "A log older than two weeks is deleted.");
            AssertTrue(File.Exists(recent) && File.Exists(edge), "Recent logs are kept.");
            AssertTrue(File.Exists(unrelated), "Files that are not translator logs are never touched.");
            AssertEqual(0, LogRetention.PruneOldLogs(Path.Combine(folder, "missing"), TimeSpan.FromDays(14), now), "A missing folder is not an error.");

            AssertTrue(LogRetention.HasRoom(Path.Combine(folder, "none.log"), 10), "A new log has room.");
            File.WriteAllText(Path.Combine(folder, "big.log"), new string('x', 100));
            AssertFalse(LogRetention.HasRoom(Path.Combine(folder, "big.log"), 100), "A log at its cap takes no more lines.");
            AssertTrue(LogRetention.HasRoom(Path.Combine(folder, "big.log"), 101), "A log under its cap still has room.");
        }
        finally { Directory.Delete(folder, recursive: true); }

        return Task.CompletedTask;
    }

    private static Task PhonePageAllowsOnlyItsOwnScriptAndStyle()
    {
        const string script = "document.title='x';\nconsole.log(1);\n";
        const string style = "body{margin:0}";
        var html = $"<!doctype html><html><head><style>{style}</style></head><body><script>{script}</script></body></html>";

        static string Hash(string text) => "'sha256-" + Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text))) + "'";
        var policy = PhonePageSecurity.ContentSecurityPolicy(html);
        AssertTrue(policy.Contains("script-src " + Hash(script) + ";", StringComparison.Ordinal), "The script is allowed by its hash and nothing else.");
        AssertTrue(policy.Contains("style-src " + Hash(style) + ";", StringComparison.Ordinal), "The style is allowed by its hash and nothing else.");
        AssertTrue(policy.StartsWith("default-src 'none';", StringComparison.Ordinal), "Everything not named is refused.");
        AssertFalse(policy.Contains("unsafe-inline", StringComparison.Ordinal) || policy.Contains("unsafe-eval", StringComparison.Ordinal), "No unsafe allowances.");
        foreach (var directive in new[] { "connect-src 'self'", "base-uri 'none'", "form-action 'none'", "frame-ancestors 'none'" })
        {
            AssertTrue(policy.Contains(directive, StringComparison.Ordinal), directive + " is part of the policy.");
        }

        var windowsLineEndings = html.Replace("\n", "\r\n");
        AssertEqual(policy, PhonePageSecurity.ContentSecurityPolicy(windowsLineEndings), "A browser hashes line feeds, so CRLF source must give the same policy.");
        AssertFalse(PhonePageSecurity.ContentSecurityPolicy(html.Replace("console.log(1)", "alert(1)")) == policy, "Changing the script changes the policy.");
        AssertTrue(PhonePageSecurity.ContentSecurityPolicy("<html><body>no script</body></html>").Contains("script-src 'none'", StringComparison.Ordinal),
            "A page with no script allows none.");

        var headers = PhonePageSecurity.ResponseHeaders(html);
        AssertEqual("nosniff", headers["X-Content-Type-Options"], "Content types are not sniffed.");
        AssertEqual("no-referrer", headers["Referrer-Policy"], "The PIN in the address is never sent on as a referrer.");
        AssertEqual("DENY", headers["X-Frame-Options"], "The page cannot be framed.");
        AssertEqual("no-store", headers["Cache-Control"], "Captions are not cached.");
        AssertEqual(policy, headers["Content-Security-Policy"], "The header carries the policy.");
        return Task.CompletedTask;
    }

    private static Task LanguagePackExtractionRefusesPathTraversal()
    {
        // Language packs are unpacked with ZipFile.ExtractToDirectory; the app relies on it refusing entries that climb out of the folder.
        var folder = Path.Combine(Path.GetTempPath(), "vrt-zip-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(folder, "pack");
        Directory.CreateDirectory(target);
        try
        {
            var archivePath = Path.Combine(folder, "evil.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(archivePath, System.IO.Compression.ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("..\\escaped.txt").Open());
                writer.Write("outside");
            }

            var refused = false;
            try { System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, target, overwriteFiles: true); }
            catch (IOException) { refused = true; }
            AssertTrue(refused, "An entry that climbs out of the pack folder must be refused.");
            AssertFalse(File.Exists(Path.Combine(folder, "escaped.txt")), "Nothing may be written outside the pack folder.");
        }
        finally { Directory.Delete(folder, recursive: true); }

        return Task.CompletedTask;
    }

    private static Task PowerShellIsStartedByFullPath()
    {
        var path = LocalTextTranslator.PowerShellPath;
        AssertTrue(Path.IsPathFullyQualified(path), "PowerShell must be started by its full path.");
        AssertTrue(path.StartsWith(Environment.SystemDirectory, StringComparison.OrdinalIgnoreCase), "PowerShell comes from the Windows system folder.");
        AssertTrue(File.Exists(path), "Windows PowerShell exists at that path.");
        return Task.CompletedTask;
    }
}
