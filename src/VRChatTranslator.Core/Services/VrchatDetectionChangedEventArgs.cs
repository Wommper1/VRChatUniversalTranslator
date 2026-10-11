using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Core.Services;

public sealed class VrchatDetectionChangedEventArgs(VrchatDetectionSnapshot snapshot) : EventArgs
{
    public VrchatDetectionSnapshot Snapshot { get; } = snapshot;
}

