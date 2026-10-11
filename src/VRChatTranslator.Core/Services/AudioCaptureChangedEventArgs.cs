using VRChatTranslator.Core.Models;

namespace VRChatTranslator.Core.Services;

public sealed class AudioCaptureChangedEventArgs(AudioCaptureSnapshot snapshot) : EventArgs
{
    public AudioCaptureSnapshot Snapshot { get; } = snapshot;
}

