using System.IO;
using VRChatTranslator.Core.Services;

namespace VRChatTranslator.App.Services;

public sealed class DiagnosticLog
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _pruned;

    public string LogDirectory { get; } = AppPaths.LogsDirectory;

    public async Task WriteAsync(string category, string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var path = Path.Combine(LogDirectory, $"translator-{DateTime.UtcNow:yyyyMMdd}.log");
            var line = $"{DateTimeOffset.Now:O} [{category}] {message}{Environment.NewLine}";
            await _gate.WaitAsync();
            try
            {
                if (!_pruned)
                {
                    // Once per run: logs older than two weeks are removed so the folder cannot grow without limit.
                    _pruned = true;
                    LogRetention.PruneOldLogs(LogDirectory, LogRetention.DefaultMaxAge, DateTime.UtcNow);
                }

                // One day's log is capped; a runaway error loop must not fill the disk.
                if (LogRetention.HasRoom(path, LogRetention.DefaultMaxFileBytes))
                {
                    await File.AppendAllTextAsync(path, line);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch
        {
            // Logging is diagnostic-only and must never break capture or transcription.
        }
    }
}
