namespace VRChatTranslator.Core.Models;

public sealed record ProcessSnapshot(
    int ProcessId,
    string ProcessName,
    DateTimeOffset? StartedAt);

