using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Core.Services;

public interface IProcessSnapshotProvider
{
    IReadOnlyList<ProcessSnapshot> GetProcessesByName(string executableNameWithoutExtension);
}

