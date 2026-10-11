using System.Diagnostics;
using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Core.Services;

public sealed class SystemProcessSnapshotProvider : IProcessSnapshotProvider
{
    public IReadOnlyList<ProcessSnapshot> GetProcessesByName(string executableNameWithoutExtension)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNameWithoutExtension);

        var processes = Process.GetProcessesByName(executableNameWithoutExtension);
        var snapshots = new List<ProcessSnapshot>(processes.Length);

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    snapshots.Add(new ProcessSnapshot(
                        process.Id,
                        process.ProcessName,
                        TryGetStartTime(process)));
                }
                catch (InvalidOperationException)
                {
                    // The process exited between enumeration and inspection.
                }
            }
        }

        return snapshots;
    }

    private static DateTimeOffset? TryGetStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}

