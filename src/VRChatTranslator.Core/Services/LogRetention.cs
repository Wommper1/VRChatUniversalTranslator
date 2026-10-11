namespace VRChatTranslator.Core.Services;

/// <summary>Keeps the diagnostic log folder from growing forever: old daily logs are removed and one day's log is capped.</summary>
public static class LogRetention
{
    public const string FilePattern = "translator-*.log";
    public static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(14);
    public const long DefaultMaxFileBytes = 5L * 1024 * 1024;

    /// <summary>Deletes translator log files last written more than <paramref name="maxAge"/> ago. Returns how many were removed.</summary>
    public static int PruneOldLogs(string directory, TimeSpan maxAge, DateTime utcNow)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(directory, FilePattern))
        {
            try
            {
                if (utcNow - File.GetLastWriteTimeUtc(file) > maxAge)
                {
                    File.Delete(file);
                    removed++;
                }
            }
            catch (IOException)
            {
                // A log that is open or locked is simply left for next time.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return removed;
    }

    /// <summary>True when appending would keep the file within <paramref name="maxBytes"/>.</summary>
    public static bool HasRoom(string path, long maxBytes) => !File.Exists(path) || new FileInfo(path).Length < maxBytes;
}
